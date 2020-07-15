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
    /// Retrieval and storage of information about an existing Lambda function
    /// </summary>
    class LambdaInfo
    {
        public string Arn { get; set; }
        public string HandlerName { get; set; }
        public string RoleName { get; set; }

        /// <summary>
        /// Retrieve and store information about an existing Lambda function
        /// </summary>
        /// <param name="LambdaName"></param>
        /// <param name="region"></param>
        public async Task GetInfoAsync(string LambdaName, RegionEndpoint region)
        {
            // Retrieve information about the Lambda function
            var lambdaClient = new AmazonLambdaClient(region);
            try
            {
                // Retrieve and store the info
                var response = await lambdaClient.GetFunctionAsync(LambdaName);
                Arn = response.Configuration.FunctionArn;
                HandlerName = response.Configuration.Handler;
                RoleName = response.Configuration.Role;
            }
            catch (AggregateException aex)
            {
                foreach (Exception ex in aex.InnerExceptions)
                    Log.Error("GetInfoAsync: " + ex.Message);
                throw;
            }
        }
    }

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
            var lambdaInfo = new LambdaInfo();
            var infoResponse = lambdaInfo.GetInfoAsync(LambdaName, region);

            // Create EventBridge rule with the desired schedule
            var eventRule = CreateRuleWithScheduleAsync(EventRuleName, EventSchedule, region);

            // Wait for Lambda info and event rule tasks
            infoResponse.Wait();
            var eventArn = eventRule.Result;

            // Debug
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
                Log.Error(msg);
                throw new AmazonEventBridgeException(msg);
            }
        }

        /// <summary>
        /// Unschedule a Lambda function, deleting the scheduled event resources
        /// </summary>
        /// <param name="LambdaName"></param>
        /// <param name="EventRuleName"></param>
        /// <param name="region">AWS region in which the resources are located</param>
        /// <exception cref="AmazonEventBridgeException"></exception>
        /// <exception cref="AmazonLambdaException"></exception>
        public static void UnScheduleLambdaFunction(
            string LambdaName,
            string EventRuleName,
            RegionEndpoint region)
        {
            // Remove Lambda target from the Events scheduled rule
            // Note: Target must be removed before deleting the Lambda function or rule
            var eventsClient = new AmazonEventBridgeClient(region);
            try
            {
                var eventRemoveTask = eventsClient.RemoveTargetsAsync(new RemoveTargetsRequest
                {
                    Rule = EventRuleName,
                    Ids = new List<string> { LambdaName },
                });
                eventRemoveTask.Wait();  // Block, can't delete rule until it's removed from targets
                Log.Debug($"Removed target {LambdaName} from scheduled rule {EventRuleName}");
            }
            catch (AggregateException aex)
            {
                // Log the exception(s) and fall through, don't throw
                foreach (Exception ex in aex.InnerExceptions)
                    Log.Error("UnScheduleLambdaFunction: " + ex.Message);
            }

            // Delete the EventBridge rule
            try
            {
                var deleteRuleTask = eventsClient.DeleteRuleAsync(new DeleteRuleRequest
                {
                    Name = EventRuleName
                });
                deleteRuleTask.Wait();
                Log.Debug("Deleted scheduled rule");
            }
            catch (AggregateException aex)
            {
                // Log the exception(s) and fall through
                foreach (Exception ex in aex.InnerExceptions)
                    Log.Error(ex.Message);
            }

            // Remove EventBridge-invoke permission from the Lambda function
            RemoveLambdaInvokePermission(LambdaName, region);
            Log.Debug("Removed invoke permission");
        }

        /// <summary>
        /// Enable/disable the schedule event
        /// </summary>
        /// <param name="EventRuleName"></param>
        /// <param name="region"></param>
        /// <exception cref="AmazonEventBridgeException"></exception>
        public static string ToggleScheduledLambda(string EventRuleName, RegionEndpoint region)
        {
            var eventsClient = new AmazonEventBridgeClient(region);
            try
            {
                // Retrieve the schedule-event rule's current state
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
                    Log.Error(ex.Message);
                }
                throw new AmazonEventBridgeException($"ToggleScheduledLambda: Could not retrieve scheduled event rule {EventRuleName}");
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
            // Create an EventBridge rule with the desired schedule
            var eventsClient = new AmazonEventBridgeClient(region);
            try
            {
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
                throw;
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
                throw;
            }
            */
        }

        /// <summary>
        /// Add permission to the Lambda function so it can be invoked by EventBridge
        /// </summary>
        /// <param name="LambdaName"></param>
        /// <param name="EventArn">EventBridge rule ARN to grant permissions for</param>
        /// <param name="region"></param>
        private static async Task AddLambdaInvokePermission(
            string LambdaName,
            string EventArn,
            RegionEndpoint region)
        {
            var lambdaClient = new AmazonLambdaClient(region);
            try
            {
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
                throw;
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
            var lambdaClient = new AmazonLambdaClient(region);
            try
            {
                // Remove invoke permission
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
        private static async Task<PutTargetsResponse> SetEventTarget(
            string EventRuleName,
            string LambdaName,
            string LambdaArn,
            RegionEndpoint region)
        {
            var eventClient = new AmazonEventBridgeClient(region);
            List<Target> targets = new List<Target>()
            {
                new Target(){ Id = LambdaName, Arn = LambdaArn },
            };
            try
            {
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
                throw;
            }
        }
    }
}
