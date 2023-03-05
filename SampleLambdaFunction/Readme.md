# Sample AWS Lambda Function

The `SampleLambdaFunction` project provides a simple AWS Lambda function written in C# for .NET Core. The function 
is used by the `ScheduleLambdaFunction` application to demonstrate how to configure a Lambda function to be invoked 
on a schedule.

## Source code files

* `Function.cs` : Simple Lambda function. Logs its input arguments and returns a JSON "SUCCESS" string.

## Build the project

Compile and deploy the sample Lambda function like any other C# Lambda function. For convenience, the project 
provides files to be used with the [AWS Toolkit for Visual Studio](https://aws.amazon.com/visualstudio/).  
