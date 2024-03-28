using ScapeCore.Core.Tools;
using ScapeCore.Traceability.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ScapeCore.Traceability.Syntax
{
    public class CommandParser(TerminalLogger linkedLogger) : IScapeCoreService
    {
        public TerminalLogger LinkedLogger { get; init; } = linkedLogger;

        public string Name => LinkedLogger.Name;

        public Guid Id => ((IScapeCoreService)LinkedLogger).Id;

        bool _isRunning = true;
        public readonly List<Command> AvailableCommands = [];

        ~CommandParser() => _isRunning = false;
        internal async Task CommandParsingLoop()
        {
            await Task.Run(async () =>
            {
                while (_isRunning)
                {
                    var cd = LinkedLogger.Directory.FullName;
                    Monitor.Enter(TerminalLogger.Lock);
                    try
                    {
                        Console.Write($" > (\\scapecore{Directory.GetCurrentDirectory().Remove(0, cd.Length)}) ");
                    }
                    finally
                    {
                        Monitor.Exit(TerminalLogger.Lock);
                    }

                    var input = Console.ReadLine()?.Trim();
                    LinkedLogger.Log("input", input, true);

                    if (string.IsNullOrEmpty(input)) continue;

                    var count = await ParseWordStringToCommandAndExecute(new WordString(input));

                    Console.WriteLine($"{count} words were processed successfully...");
                }
            });
        }

        private async Task<int> ParseWordStringToCommandAndExecute(WordString input) => await Task.Run( () => input.Contains(":") && !input.ElementAt(0).Contains(':') ? ParallelParser(input).wordCounter : SimpleParser(input));

        public record class ParallelParserResult
        {
            public ParallelLoopResult result;
            public int wordCounter;
        }
        private ParallelParserResult ParallelParser(WordString input)
        {
            var lastIndex = -1;
            var pipes = new List<WordString>();

            while (true)
            {
                var index = input.ToList().IndexOf(":", lastIndex + 1);
                bool isLast = index == lastIndex || index == -1;

                var joinedCommand = isLast ?
                    string.Join(' ', input.Skip(input.ToList().IndexOf(":", lastIndex) + 1)) :
                    string.Join(' ', input.Select((x, i) =>
                    {
                        if (i > (lastIndex == 0 ? -1 : lastIndex) && i < index)
                            return x;
                        return string.Empty;
                    })).Trim();

                pipes.Add(new(joinedCommand));

                if (isLast)
                    break;

                lastIndex = index;
            }

            ParallelParserResult result = new();
            result.result = Parallel.ForEach(pipes, () => 0, (pipe, state, localCount) =>
            {
                localCount += SimpleParser(pipe);
                return localCount;
            }, localCount =>
            {
                Interlocked.Add(ref result.wordCounter, localCount);
            });

            return result;
        }

        readonly record struct CommandParametersPair(Command? Command, List<object>? Parameters);
        private int SimpleParser(WordString input)
        {
            bool skipCurrentCommand = false;
            int currentCommandNumberExecutionParameters = 0;
            int wordCount = 0;

            List<CommandParametersPair> commandsToExecute = [];
            Command? currentCommand = null;
            List<object> currentCommandParameters = [];

            foreach (var word in input)
            {
                if (word == "|")
                {
                    ExecuteAndClear(commandsToExecute);
                    currentCommand = null;
                    continue;
                }

                wordCount++;

                currentCommand ??= AvailableCommands.Find(x => x.Info.Name.Equals(word, StringComparison.OrdinalIgnoreCase));

                if (currentCommand != null && currentCommand!.Info.Name != null)
                {
                    var subCommand = currentCommand.Subcommands?.ToList().Find(x => x.Info.Name.Equals(word, StringComparison.OrdinalIgnoreCase));

                    if (subCommand != null && subCommand!.Info.Name != null)
                        currentCommand = subCommand;

                    if (currentCommand!.Info.NumberOfExecutionParameters > 0)
                    {
                        if (!word.Equals(currentCommand.Info.Name, StringComparison.OrdinalIgnoreCase))
                            currentCommandParameters.Add(word);
                        if (currentCommandNumberExecutionParameters++ == currentCommand.Info.NumberOfExecutionParameters)
                        {
                            if (!commandsToExecute.Contains(new(currentCommand, new(currentCommandParameters))))
                            {
                                commandsToExecute.Add(new(currentCommand, new(currentCommandParameters)));
                                currentCommandNumberExecutionParameters = 0;
                                currentCommandParameters.Clear();
                                if (currentCommand!.Subcommands!.Length <= 0)
                                    currentCommand = currentCommand.Parent ?? currentCommand;
                            }
                        else
                        {
                            LinkedLogger.Log("error", $"{new LoggingColor(255, 32, 22)}Subcommand {word} does not exist. Did you mispelled something?{LoggingColor.Normal}");
                            skipCurrentCommand = true;
                            break;
                        }
                        }
                    }
                    else
                    {
                        if (!commandsToExecute.Contains(new(currentCommand, null)))
                        {
                            commandsToExecute.Add(new(currentCommand, null));
                            if (currentCommand!.Subcommands!.Length <= 0)
                                currentCommand = currentCommand.Parent ?? currentCommand;
                        }
                        else
                        {
                            LinkedLogger.Log("error", $"{new LoggingColor(255, 32, 22)}Subcommand {word} does not exist. Did you mispelled something?{LoggingColor.Normal}");
                            skipCurrentCommand = true;
                            break;
                        }
                    }
                }
                else
                {
                    LinkedLogger.Log("error", $"{new LoggingColor(255, 32, 22)}Command {word} does not exist. Did you mispelled something?{LoggingColor.Normal}");
                    skipCurrentCommand = true;
                    break;
                }
            }

            if (skipCurrentCommand)
                commandsToExecute.Clear();

            ExecuteAndClear(commandsToExecute);

            return wordCount;
        }

        private void ExecuteAndClear(List<CommandParametersPair> commands)
        {
            while (commands.Count > 0)
            {
                var item = commands.Last();
                if (item.Command != null)
                {
                    if (!item.Command!.suppress)
                        item.Command!.DefaultExecution(item.Parameters != null ? [.. item.Parameters] : null);
                    item.Command!.suppress = false;
                    commands.Remove(item);
                }
            }
            commands.Clear();
        }
    }
}