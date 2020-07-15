using Amazon;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Serilog;
using System;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScheduleLambdaFunction
{
    /// <summary>
    /// Retrieve and store information about an existing Lambda function
    /// </summary>
    class LambdaInfo
    {
        public string Arn { get; }
        public string HandlerName { get; }
        public string RoleName { get; }


        /// <summary>
        /// Initialize object with information about an existing Lambda function
        /// </summary>
        /// <param name="LambdaName"></param>
        /// <param name="region"></param>
        /// <exception cref="AmazonLambdaException"></exception>"
        public LambdaInfo(string LambdaName, RegionEndpoint region)
        {
            try
            {
                // Retrieve and store information about the Lambda function
                var lambdaClient = new AmazonLambdaClient(region);
                var infoTask = lambdaClient.GetFunctionAsync(LambdaName);
                var response = infoTask.Result;
                Arn = response.Configuration.FunctionArn;
                HandlerName = response.Configuration.Handler;
                RoleName = response.Configuration.Role;
            }
            catch (AggregateException aex)
            {
                foreach (Exception ex in aex.InnerExceptions)
                {
                    Log.Error("LambdaInfo constructor: " + ex.Message);
                }
                throw aex.InnerException;
            }
        }
    }


    /// <summary>
    /// Create and manage the invoking of an AWS Lambda function based on a schedule
    /// </summary>
    /// <remarks>
    ///     Entry points:
    ///         ScheduleLambdaFunction
    ///         UnScheduleLambdaFunction (also deletes the schedule resources)
    ///         ToggleScheduledLambda  (enables/disables schedule; does not delete resources)
    /// </remarks>
    class ScheduleLambda
    {

        /// <summary>
        /// Schedule the repeated invocation of an existing AWS Lambda function
        /// </summary>
        /// <param name="LambdaName"></param>
        /// <param name="EventRuleName">Name of EventBridge scheduled rule to create</param>
        /// <param name="EventSchedule">cron-formatted event schedule</param>
        /// <param name="region">AWS region in which the Lambda is located</param>
        /// <exception cref="AmazonEventBridgeException"></exception>
        /// <exception cref="AmazonLambdaException"></exception>
        public static void ScheduleLambdaFunction(
            string LambdaName,
            string EventRuleName,
            string EventSchedule,
            RegionEndpoint region)
        {
            // Retrieve information about the Lambda function
            var lambdaInfo = new LambdaInfo(LambdaName, region);

            // Create EventBridge rule with the desired schedule
            var eventRule = CreateRuleWithScheduleAsync(EventRuleName, EventSchedule, region);
            var eventArn = eventRule.Result;

            // Make sure we have reasonable values
            Log.Debug("FunctionInfo.Arn: " + lambdaInfo.Arn);
            Log.Debug("FunctionInfo.Handler: " + lambdaInfo.HandlerName);
            Log.Debug("FunctionInfo.Role: " + lambdaInfo.RoleName);
            Log.Debug("EventRule.Arn: " + eventArn);

            // Grant invoke permissions on the Lambda function so it can be called by
            // EventBridge/CloudWatch Events.
            // Note: To retrieve the Lambda function's permissions, call LambdaClient.GetPolicy()
            var permTask = AddLambdaInvokePermission(LambdaName, eventArn, region);
            permTask.Wait();  // Block

            // Add the Lambda function as the target of the scheduled event rule
            var targetTask = SetEventTarget(EventRuleName, LambdaName, lambdaInfo.Arn, region);
            var response = targetTask.Result;
            if (response.FailedEntryCount != 0)
            {
                var msg = $"Could not set {LambdaName} as the target for {EventRuleName}";
                Log.Error("ScheduleLambdaFunction: " + msg);
                throw new AmazonEventBridgeException(msg);
            }
        }


        /// <summary>
        /// Unschedule a Lambda function, deleting the scheduled event resources
        /// </summary>
        /// <param name="LambdaName"></param>
        /// <param name="EventRuleName"></param>
        /// <param name="region">AWS region in which the resources are located</param>
        public static void UnScheduleLambdaFunction(
            string LambdaName,
            string EventRuleName,
            RegionEndpoint region)
        {
            try
            {
                // Remove Lambda target from the Events scheduled rule
                // Note: Target must be removed before deleting the Lambda function or event rule
                var eventsClient = new AmazonEventBridgeClient(region);
                var eventRemoveTask = eventsClient.RemoveTargetsAsync(new RemoveTargetsRequest
                {
                    Rule = EventRuleName,
                    Ids = new List<string> { LambdaName },
                });
                eventRemoveTask.Wait();  // Block, can't delete rule until it's removed from targets
                Log.Debug($"Removed target {LambdaName} from scheduled rule {EventRuleName}");

                // Delete the EventBridge rule
                var deleteRuleTask = eventsClient.DeleteRuleAsync(new DeleteRuleRequest
                {
                    Name = EventRuleName
                });
                deleteRuleTask.Wait();
                Log.Debug("Deleted scheduled rule");

                // Remove EventBridge-invoke permission from the Lambda function
                RemoveLambdaInvokePermission(LambdaName, region);
                Log.Debug("Removed invoke permission");
            }
            catch (AggregateException aex)
            {
                // Log the exception(s) and fall through, don't throw
                foreach (Exception ex in aex.InnerExceptions)
                    Log.Error("UnScheduleLambdaFunction: " + ex.Message);
            }
        }


        /// <summary>
        /// Enable/disable the schedule event
        /// </summary>
        /// <param name="EventRuleName"></param>
        /// <param name="region"></param>
        /// <exception cref="AmazonEventBridgeException"></exception>
        public static string ToggleScheduledLambda(string EventRuleName, RegionEndpoint region)
        {
            try
            {
                // Retrieve the schedule-event rule's current state
                var eventsClient = new AmazonEventBridgeClient(region);
                var describeTask = eventsClient.DescribeRuleAsync(new DescribeRuleRequest
                {
                    Name = EventRuleName,
                });

                // Toggle state and return the new state
                if (describeTask.Result.State.Value.Equals(RuleState.ENABLED))
                {
                    var disableTask = eventsClient.DisableRuleAsync(new DisableRuleRequest
                    {
                        Name = EventRuleName,
                    });
                    disableTask.Wait();
                    return "DISABLED";
                }
                else
                {
                    var enableTask = eventsClient.EnableRuleAsync(new EnableRuleRequest
                    {
                        Name = EventRuleName,
                    });
                    enableTask.Wait();
                    return "ENABLED";
                }
            }
            catch (AggregateException aex)
            {
                foreach (Exception ex in aex.InnerExceptions)
                {
                    Log.Error("ToggleScheduledLambda: " + ex.Message);
                }
                throw aex.InnerException;
            }
        }


        /// <summary>
        /// Define an EventBridge rule with a schedule
        /// </summary>
        /// <param name="EventRuleName">Name of EventBridge rule to create</param>
        /// <param name="EventSchedule">cron-formatted event schedule</param>
        /// <param name="region">AWS region in which to locate the rule</param>
        /// <returns>ARN of EventBridge rule</returns>
        /// <exception cref="AmazonEventBridgeException"></exception>
        private static async Task<string> CreateRuleWithScheduleAsync(
            string EventRuleName,
            string EventSchedule,
            RegionEndpoint region)
        {
            try
            {
                // Create an EventBridge rule with the desired schedule
                var eventsClient = new AmazonEventBridgeClient(region);
                var putRuleResponse = await eventsClient.PutRuleAsync(new PutRuleRequest
                {
                    Name = EventRuleName,
                    ScheduleExpression = EventSchedule,
                });
                return putRuleResponse.RuleArn;
            }
            catch (AggregateException aex)
            {
                foreach (Exception ex in aex.InnerExceptions)
                    Log.Error("CreateRuleWithScheduleAsync: " + ex.Message);
                throw aex.InnerException;
            }

            /*
            // Alternatively, use Task<T>...
            // Task<PutRuleResponse> ruleResponse;
            var ruleResponse = eventsClient.PutRuleAsync(eventReq);
            // ... do some stuff ...
            string eventRuleArn2;
            try
            {
                eventRuleArn2 = (await ruleResponse).RuleArn;
            }
            catch (AggregateException aex)
            {
                foreach (Exception ex in aex.InnerExceptions)
                    Log.Error("CreateRuleWithScheduleAsync: " + ex.Message);
                throw aex.InnerException;
            }
            */
        }


        /// <summary>
        /// Add permission to the Lambda function so it can be invoked by EventBridge
        /// </summary>
        /// <param name="LambdaName"></param>
        /// <param name="EventArn">EventBridge rule ARN to grant permissions for</param>
        /// <param name="region"></param>
        /// <exception cref="AmazonLambdaException"></exception>"
        private static async Task AddLambdaInvokePermission(
            string LambdaName,
            string EventArn,
            RegionEndpoint region)
        {
            try
            {
                // Add invoke permission
                var lambdaClient = new AmazonLambdaClient(region);
                await lambdaClient.AddPermissionAsync(new AddPermissionRequest
                {
                    FunctionName = LambdaName,
                    StatementId = $"{LambdaName}-invoke",
                    Action = "lambda:InvokeFunction",
                    Principal = "events.amazonaws.com",
                    SourceArn = EventArn,
                });
            }
            catch (AggregateException aex)
            {
                foreach (Exception ex in aex.InnerExceptions)
                    Log.Error("AddLambdaInvokePermission: " + ex.Message);
                throw aex.InnerException;
            }
        }


        /// <summary>
        /// Remove permission for EventBridge to invoke the Lambda function
        /// </summary>
        /// <param name="LambdaName"></param>
        /// <param name="region"></param>
        private static void RemoveLambdaInvokePermission(
            string LambdaName,
            RegionEndpoint region)
        {
            try
            {
                // Remove invoke permission
                var lambdaClient = new AmazonLambdaClient(region);
                var permTask = lambdaClient.RemovePermissionAsync(new Amazon.Lambda.Model.RemovePermissionRequest
                {
                    FunctionName = LambdaName,
                    StatementId = $"{LambdaName}-invoke",
                });
                var statusCode = permTask.Result.HttpStatusCode;
                if (statusCode != HttpStatusCode.NoContent)
                {
                    Log.Error("RemoveLambdaInvokePermission: Could not remove permission: HTTP status: " + statusCode.ToString());
                }
            }
            catch (AggregateException aex)
            {
                // Log exception(s) and fall through, do not throw
                foreach (Exception ex in aex.InnerExceptions)
                    Log.Error("RemoveLambdaInvokePermission: " + ex.Message);
            }
        }


        /// <summary>
        /// Set the Lambda function as the target of the scheduled event rule
        /// </summary>
        /// <param name="EventRuleName"></param>
        /// <param name="LambdaName"></param>
        /// <param name="LambdaArn"></param>
        /// <param name="region"></param>
        /// <returns>PutTargetsResponse object</returns>
        /// <exception cref="AmazonEventBridgeException"></exception>"
        private static async Task<PutTargetsResponse> SetEventTarget(
            string EventRuleName,
            string LambdaName,
            string LambdaArn,
            RegionEndpoint region)
        {
            try
            {
                var eventClient = new AmazonEventBridgeClient(region);
                List<Target> targets = new List<Target>()
                {
                    new Target(){ Id = LambdaName, Arn = LambdaArn },
                };
                return await eventClient.PutTargetsAsync(new PutTargetsRequest
                {
                    Rule = EventRuleName,
                    Targets = targets,
                });
            }
            catch (AggregateException aex)
            {
                foreach (Exception ex in aex.InnerExceptions)
                    Log.Error("SetEventTarget: " + ex.Message);
                throw aex.InnerException;
            }
        }
    }
}
