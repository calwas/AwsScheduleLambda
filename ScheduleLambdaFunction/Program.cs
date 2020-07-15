using System;
using System.Collections.Generic;
// using System.Threading.Tasks;
using Amazon;
// using Amazon.S3;
// using Amazon.S3.Model;
using CommandLine;
using Serilog;

namespace ScheduleLambdaFunction
{

    class Program
    {
        // Default configuration information. Change as desired
        static readonly string lambdaName = "ScheduledFunction";  // Name of existing Lambda function to schedule
        static readonly string eventRuleName = "scheduled_lambda_rule";  // Scheduled event rule to create
        static readonly string eventSchedule = "cron(0/1 * * * ? *)";  // Trigger every minute of every day
        static readonly RegionEndpoint region = RegionEndpoint.USWest1;

        static void Main(string[] args)
        {

            // Init logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .CreateLogger();

            // Process command-line arguments
            CmdLineOptions.ParseCmdLineOptions(args)
                .WithParsed(ParsedOptions)
                .WithNotParsed(ParsedErrors);

            // Create a Lambda function that is invoked on a schedule
            try
            {
                Log.Information($"Scheduling Lambda function {lambdaName}...");
                ScheduleLambda.ScheduleLambdaFunction(lambdaName, eventRuleName, eventSchedule, region);
                Log.Information("Done");
            }
            catch
            {
                Log.Information("Operation failed");
                Environment.Exit(1);
            }
        }

        static void ParsedOptions(CmdLineOptions options)
        {
            if (options.Delete)
            {
                // Unschedule the Lambda function and delete the schedule resources
                ScheduleLambda.UnScheduleLambdaFunction(lambdaName, eventRuleName, region);
                Log.Information("Deleted all schedule resources");
                Environment.Exit(0);
            }

            if (options.Toggle)
            {
                // Toggle the state of the scheduled event (enable/disable)
                var state = ScheduleLambda.ToggleScheduledLambda(eventRuleName, region);
                Log.Information("Toggled scheduled Lambda function to " + state);
                Environment.Exit(0);
            }
        }

        static void ParsedErrors(IEnumerable<Error> errs)
        {
            var result = -1;  // Default program exit value
            foreach (var err in errs)
            {
                // --version or --help
                if (err is VersionRequestedError || err is HelpRequestedError)
                {
                    result = 0;  // CommandLineParser processes these options
                }
            }
            Environment.Exit(result);
        }

        /*
         * Simple S3 test to monitor the flow of async/await code
         */
        /*
        static void TestAsyncAwait()
        {
            var task = ListS3BucketsAsync();
            var response = task.Result;
            Console.WriteLine("Main: Returned from ListS3Buckets()");
            Console.WriteLine("Main: Bucket owner: " + response.Owner.DisplayName);
            foreach (var bucket in response.Buckets)
            {
                Console.WriteLine($"Main: Bucket {bucket.BucketName}, Created on {bucket.CreationDate}");
            }
        }

        static async Task<ListBucketsResponse> ListS3BucketsAsync()
        {
            var client = new AmazonS3Client(RegionEndpoint.USWest1);
            var response = await client.ListBucketsAsync();
            Console.WriteLine("Bucket owner: " + response.Owner.DisplayName);
            foreach (var bucket in response.Buckets)
            {
                Console.WriteLine($"Bucket {bucket.BucketName}, Created on {bucket.CreationDate}");
            }
            return response;
        }
        */
    }
}
