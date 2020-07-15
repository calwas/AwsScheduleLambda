using System.IO;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace SampleLambdaFunction
{
    public class Function
    {
        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="@event">
        ///   <para>EventBridge scheduled event in JSON</para>
        ///   <para>
        ///     {
        ///         "version": "0",
        ///         "id": "08717fbd-...",
        ///         "detail-type": "Scheduled Event",
        ///         "source": "aws.events",
        ///         "account" "123456...",
        ///         "time": "2020-07-14T20:15:00Z",
        ///         "region": "us-west-1",
        ///         "resources": [
        ///             "arn:aws:events:us-west-1:123456...:rule/scheduled_lambda_rule"
        ///         ],
        ///         "detail": { }
        ///     }
        ///   </para>
        /// </param>
        /// <param name="context"></param>
        /// <returns></returns>
        public string ScheduledFunctionHandler(Stream @event, ILambdaContext context)
        {
            // Convert input stream to JSON string
            byte[] buffer = new byte[@event.Length];
            @event.Read(buffer, 0, (int)@event.Length);
            string myevent = Encoding.UTF8.GetString(buffer);

            // Log some info
            LambdaLogger.Log("EVENT: " + myevent);
            LambdaLogger.Log($"Function name: {context.FunctionName}, ARN: {context.InvokedFunctionArn}");
            LambdaLogger.Log("CONTEXT: " + JsonSerializer.Serialize(context));

            // The remainder are some sample logging statements for research purposes
            // FYI: LambdaLogger execution time: 88ms, context.Logger: 98ms, Console.WriteLine: 700ms
            // var logger = context.Logger;
            // logger.Log("CONTEXT: " + context);
            // logger.Log($"Function name: {context.FunctionName}, ARN: {context.InvokedFunctionArn}");
            // Console.WriteLine("Function name: " + context.FunctionName);

            // logger.Log("EVENT Length: " + input.Length);
            // logger.Log("EVENT2: " + JsonConvert.SerializeObject(myevent));  // Takes much time
            // logger.Log("EVENT: " + JsonConvert.SerializeObject(input.Read(buffer, 0, 1024)));
            // LambdaLogger.Log("EVENT2: " + JsonConvert.SerializeObject(input));

            // Return success JSON
            return "{\"StatusCode\": 200}";
        }
    }
}
