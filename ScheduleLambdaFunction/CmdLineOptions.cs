using CommandLine;

namespace ScheduleLambdaFunction
{
    class CmdLineOptions
    {
        [Option('d', "delete",
            Default = false,
            HelpText = "Unschedule the function and delete the schedule resources")]
        public bool Delete { get; set; }

        [Option('t', "toggle",
            Default = false,
            HelpText = "Enable/disable schedule")]
        public bool Toggle { get; set; }

        public static ParserResult<CmdLineOptions> ParseCmdLineOptions(string[] args)
        {
            // Process command-line arguments
            return Parser.Default.ParseArguments<CmdLineOptions>(args);
        }
    }
}
