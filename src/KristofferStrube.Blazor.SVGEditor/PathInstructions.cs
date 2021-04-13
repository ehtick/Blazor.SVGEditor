﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KristofferStrube.Blazor.SVGEditor
{
    public static class PathData
    {
        public static List<IPathInstruction> Parse(string input)
        {
            var strippedInput = input.Replace(",", " ").Replace("-", " -");
            List<string> instructions = new() { "M", "m", "Z", "z", "L", "l", "H", "h", "V", "v", "C", "c", "S", "s", "Q", "q", "T", "t", "A", "a" };
            var standardizedInput = instructions.Aggregate(strippedInput, (accu, curr) => accu.Replace(curr, $",{curr} ")).TrimStart(' ');
            // This part looks for any number of spaces and replaces them with a single space.
            var removesDoubleSpaces = Regex.Replace(standardizedInput, @"\s+", " ");
            var splitInstructionSequences = removesDoubleSpaces.Split(",");
            return Enumerable.Range(1, splitInstructionSequences.Length-1).Aggregate<int, List<IPathInstruction>>(
                new List<IPathInstruction>(),
                (list, curr) =>
                    {
                        var previousInstruction = curr == 1 ? null : list.Last();
                        var seq = splitInstructionSequences[curr].TrimEnd(' ');
                        var instruction = seq.Substring(0, 1);
                        var parameters = seq.Substring(2, seq.Length - 2).Split(" ").Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToList();
                        switch (instruction)
                        {
                            case "L":
                                if (parameters.Count % 2 != 0)
                                    throw new ArgumentException($"Wrong number of parameters for 'L' at number {curr} sequence in {removesDoubleSpaces}");
                                Enumerable.Range(0, parameters.Count / 2).ToList().ForEach(i =>
                                {
                                    var inst = new AbsoluteLineInstruction(parameters[i * 2], parameters[i * 2 + 1]) { PreviousInstruction = previousInstruction, ExplicitSymbol = i == 0 };
                                    list.Add(inst);
                                    if (previousInstruction is not null)
                                    {
                                        previousInstruction.NextInstruction = inst;
                                    }
                                    previousInstruction = inst;
                                });
                                break;
                            case "l":
                                if (parameters.Count % 2 != 0)
                                    throw new ArgumentException($"Wrong number of parameters for 'L' at number {curr} sequence in {removesDoubleSpaces}");
                                Enumerable.Range(0, parameters.Count / 2).ToList().ForEach(i =>
                                {
                                    var inst = new RelativeLineInstruction(parameters[i * 2], parameters[i * 2 + 1]) { PreviousInstruction = previousInstruction, ExplicitSymbol = i == 0 };
                                    list.Add(inst);
                                    if (previousInstruction is not null)
                                    {
                                        previousInstruction.NextInstruction = inst;
                                    }
                                    previousInstruction = inst;
                                });
                                break;
                            case "M":
                                if (parameters.Count % 2 != 0)
                                    throw new ArgumentException($"Wrong number of parameters for 'M' at number {curr} sequence in {removesDoubleSpaces}");
                                Enumerable.Range(0, parameters.Count / 2).ToList().ForEach(i =>
                                {
                                    var inst = new AbsoluteMoveInstruction(parameters[i * 2], parameters[i * 2 + 1]) { PreviousInstruction = previousInstruction, ExplicitSymbol = i == 0 };
                                    list.Add(inst);
                                    if (previousInstruction is not null)
                                    {
                                        previousInstruction.NextInstruction = inst;
                                    }
                                    previousInstruction = inst;
                                });
                                break;
                            case "m":
                                if (parameters.Count % 2 != 0)
                                    throw new ArgumentException($"Wrong number of parameters for 'm' at number {curr} sequence in {removesDoubleSpaces}");
                                Enumerable.Range(0, parameters.Count / 2).ToList().ForEach(i =>
                                {
                                    var inst = new RelativeMoveInstruction(parameters[i * 2], parameters[i * 2 + 1]) { PreviousInstruction = previousInstruction, ExplicitSymbol = i == 0 };
                                    list.Add(inst);
                                    if (previousInstruction is not null)
                                    {
                                        previousInstruction.NextInstruction = inst;
                                    }
                                    previousInstruction = inst;
                                });
                                break;
                            default:
                                throw new ArgumentException($"Non supported sequence initializer: {instruction}");
                        }
                        return list;
                    });
        }

        public static string AsString(this List<IPathInstruction> pathData) => string.Join(" ", pathData.Select(p => p.ToString()));

        public static string AsString(this double d) => d.ToString(CultureInfo.InvariantCulture);
    }

    public interface IPathInstruction
    {
        public IPathInstruction PreviousInstruction { get; set; }
        public IPathInstruction NextInstruction { get; set; }
        public bool ExplicitSymbol { get; set; }
        public (double x, double y) StartPosition { get; }
        public (double x, double y) EndPosition { get; set; }
        public IPathInstruction ConvertToAbsolute { get; }
        public IPathInstruction ConvertToRelative { get; }
        public string Instruction { get; }
        public string ToString();
        public bool IsRelative();
    }
    public abstract class BasePathInstruction : IPathInstruction
    {
        public IPathInstruction PreviousInstruction { get; set; }
        public IPathInstruction NextInstruction { get; set; }
        public bool ExplicitSymbol { get; set; }
        public (double x, double y) StartPosition => PreviousInstruction is not null ? PreviousInstruction.EndPosition : (0, 0);
        public abstract (double x, double y) EndPosition { get; set; }
        public abstract IPathInstruction ConvertToAbsolute { get; }
        public abstract IPathInstruction ConvertToRelative { get; }
        public abstract string Instruction { get; }
        public abstract override string ToString();
        public abstract bool IsRelative();
    }


    public class AbsoluteLineInstruction : BasePathInstruction, IPathInstruction
    {
        public AbsoluteLineInstruction(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        private double x { get; set; }

        private double y { get; set; }

        public override (double x, double y) EndPosition
        {
            get { return (x, y); }
            set { x = value.x; y = value.y; }
        }

        public override IPathInstruction ConvertToAbsolute => this;

        public override IPathInstruction ConvertToRelative => new RelativeLineInstruction(StartPosition.x - x, StartPosition.y - y);

        public override string Instruction => "L";

        public override string ToString() => (ExplicitSymbol ? $"{Instruction} " : "") + $"{x.AsString()} {y.AsString()}";

        public override bool IsRelative() => false;
    }

    public class RelativeLineInstruction : BasePathInstruction, IPathInstruction
    {
        public RelativeLineInstruction(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        private double x { get; set; }

        private double y { get; set; }

        public override (double x, double y) EndPosition
        {
            get { return (StartPosition.x + x, StartPosition.y + y); }
            set { x = value.x - StartPosition.x; y = value.y - StartPosition.y; }
        }

        public override IPathInstruction ConvertToAbsolute => new AbsoluteLineInstruction(EndPosition.x, EndPosition.y);

        public override IPathInstruction ConvertToRelative => this;

        public override string Instruction => "l";

        public override string ToString() => (ExplicitSymbol ? $"{Instruction} " : "") + $"{x.AsString()} {y.AsString()}";

        public override bool IsRelative() => true;
    }

    public class AbsoluteMoveInstruction : BasePathInstruction
    {
        public AbsoluteMoveInstruction(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        private double x { get; set; }

        private double y { get; set; }

        public override (double x, double y) EndPosition
        {
            get { return (x, y); }
            set { x = value.x; y = value.y; }
        }

        public override IPathInstruction ConvertToAbsolute => this;

        public override IPathInstruction ConvertToRelative => throw new NotImplementedException();

        public override string Instruction => "M";

        public override string ToString() => (ExplicitSymbol ? $"{Instruction} " : "") + $"{x.AsString()} {y.AsString()}";

        public override bool IsRelative() => false;
    }

    public class RelativeMoveInstruction : BasePathInstruction
    {
        public RelativeMoveInstruction(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        private double x { get; set; }

        private double y { get; set; }

        public override (double x, double y) EndPosition
        {
            get { return (StartPosition.x + x, StartPosition.y + y); }
            set { x = value.x - StartPosition.x; y = value.y - StartPosition.y; }
        }

        public override IPathInstruction ConvertToAbsolute => this;

        public override IPathInstruction ConvertToRelative => throw new NotImplementedException();

        public override string Instruction => "m";

        public override string ToString() => (ExplicitSymbol ? $"{Instruction} " : "") + $"{x.AsString()} {y.AsString()}";

        public override bool IsRelative() => true;
    }
}