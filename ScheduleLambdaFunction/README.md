# Schedule an AWS Lambda Function

The `ScheduleLambdaFunction` project demonstrates how an AWS Lambda function can be invoked automatically based on a 
time schedule.

The project creates a .NET Core 3 console application. All source files are written in C#.

The Lambda function that the application schedules can be written in any programming language. A sample Lambda 
function written in C# is provided in the repository's `SampleLambdaFunction` project.

By default, the `ScheduleLambdaFunction` configures the sample Lambda function to be invoked every 
minute. It assumes the Lambda function is located in the AWS region US-West-1 (N. California).

To configure a different Lambda function in a different region to another schedule, edit the constant values 
defined in the `Program.cs` source file. Edits to the other source files should not be necessary. 
 
## Prerequisites

* The Lambda function to schedule must already exist and be deployed on AWS

## Source code files

* `Program.cs` : Application entry point. Identifies the Lambda function, AWS region, etc. to be scheduled
* `ScheduleLambda.cs` : Create and manage the AWS resources that schedule the Lambda function
* `CmdLineOptions.cs` : Define the application's command-line options

## Package dependencies

The application is dependent on the following packages. If using Visual Studio and the NuGet package manager, the 
packages should be installed automatically the first time the project is loaded.

* AWSSDK.Core
* AWSSDK.EventBridge
* AWSSDK.Lambda
* CommandLineParser
* Serilog
* Serilog.Sinks.Console

## AWS scheduling resources

The application creates AWS scheduling resources in the same AWS region where the Lambda function exists.

* Amazon EventBridge rule : Defines the schedule on which a scheduled event occurs
* Amazon EventBridge target : Triggers the Lambda function whenever the scheduled event occurs
* AWS Lambda resource permission : Grants permission to EventBridge to invoke the function
* Amazon CloudWatch Logs : A log group to store logs from the Lambda function is created automatically by AWS

## Instructions

To schedule a Lambda function:

    ScheduleLambdaFunction

To toggle (enable/disable) the scheduled invocation of the Lambda function:

    ScheduleLambdaFunction -t
    OR
    ScheduleLambdaFunction --toggle

To unschedule the Lambda function and delete the AWS scheduling resources (does not delete CloudWatch logs):

    ScheduleLambdaFunction -d
    OR
    ScheduleLambdaFunction --delete

To show application version information:

    ScheduleLambdaFunction --version

To show application instructions:

    ScheduleLambdaFunction --help

To verify that the Lambda function is being invoked, use the AWS console to check the CloudWatch log files. AWS 
and the sample Lambda function write event data to a log file each time the function is invoked. If the log files 
are not being created or updated, verify that the associated EventBridge rule is enabled. Also check that the Lambda 
function has granted permission to be invoked by EventBridge.
 