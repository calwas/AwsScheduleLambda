using CommandLine;

namespace ScheduleLambdaFunction
{
    /// <summary>
    /// Define and parse the program's supported command-line options
    /// </summary>
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


        /// <summary>
        /// Parse the command-line options
        /// </summary>
        /// <param name="args">Array of command-line options</param>
        /// <returns></returns>
        public static ParserResult<CmdLineOptions> ParseCmdLineOptions(string[] args)
        {
            // Process command-line arguments
            return Parser.Default.ParseArguments<CmdLineOptions>(args);
        }
    }
}
