namespace Strands
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Utilities;

    internal class Program
    {
        static void Main(string[] args)
        {
            var grid = new string[]
            {
                "sjmwil",
                "teaihg",
                "edelnt",
                "vstndi",
                "ilyacn",
                "ahelhg",
                "rskave",
                "kscoha",
            };

            var allWords = File.ReadLines("words_alpha.txt").Where(x => x.Length > 3);

            var dictionaries = Dictionary.Create(allWords);

            var tasks = new List<Task<HashSet<Solution>>>();
            for (int ii = 0; ii < grid.Length; ii++)
            {
                for (int jj = 0; jj < grid[ii].Length; jj++)
                {
                    var initialPosition = new Position(jj, ii);
                    char initialChar = initialPosition.Get(grid).Value;
                    var initialDictionary = dictionaries.SingleOrDefault(x => x.Letter == initialChar);
                    if (initialDictionary == null)
                    {
                        continue;
                    }
                    tasks.Add(Task.Run(() =>
                    {
                        return Recurse(grid, new List<Position> { initialPosition }, initialDictionary);
                    }));

                }
            }

            Task.WaitAll(tasks.ToArray());
            var solutions = tasks.SelectMany(x => x.Result).Distinct();
            if (solutions.Count() == 0)
            {
                Console.WriteLine("No solutions found");
                return;
            }

            while (solutions.Count() > 0)
            {
                int index = 0;
                var solutionLookup = solutions.OrderBy(x => x.Word.Length).ThenBy(x => x.Word).ToDictionary(x => index++);
                foreach (var solutionPair in solutionLookup)
                {
                    Console.WriteLine($"{solutionPair.Key,-10} {solutionPair.Value}");
                }
Repeat:
                string input = Console.ReadLine();
                bool displayOnly = input.StartsWith("?");
                if (displayOnly)
                {
                    input = input.TrimStart("?");
                }
                index = int.Parse(input);
                var solution = solutionLookup[index];
                if (displayOnly)
                {
                    for (int ii =0; ii < grid.Length; ii++)
                    {
                        for (int jj = 0; jj < grid[ii].Length; jj++)
                        {
                            var position = new Position(jj, ii);
                            if (solution.Positions.Contains(position))
                            {
                                Console.Write(grid[ii][jj]);
                            }
                            else
                            {
                                Console.Write('-');
                            }
                        }
                        Console.WriteLine();
                    }
                    goto Repeat;
                }
                else
                {
                    foreach (var position in solution.Positions)
                    {
                        grid[position.Y] = grid[position.Y].ReplaceAtIndex(position.X, '-');
                    }

                    foreach (var line in grid)
                    {
                        Console.WriteLine(line);
                    }

                    solutions = solutions.Where(x => x.IsStillValid(grid));
                }
            }
        }

        private static HashSet<Solution> Recurse(string[] grid, List<Position> positions, Dictionary dictionary)
        {
            var result = new HashSet<Solution>();
            foreach (var direction in Position.Directions)
            {
                var newPosition = positions.Last() + direction;
                if (!newPosition.IsValid(grid) || positions.Contains(newPosition))
                {
                    continue;
                }

                var newPositions = new List<Position>(positions)
                {
                    newPosition
                };

                var newChar = newPosition.Get(grid);
                if (newChar == null)
                {
                    continue;
                }
                var newDictionary = dictionary.GetNext(newChar.Value);
                if (newDictionary.IsValid)
                {
                    result.Add(new Solution(newDictionary.Word, newPositions));
                }

                if (!newDictionary.Remainders.Any())
                {
                    continue;
                }

                result.UnionWith(Recurse(grid, newPositions, newDictionary));
            }

            return result;
        }

        internal class Solution : EqualityBase<Solution>
        {
            public string Word { get; }
            public IEnumerable<Position> Positions { get; }

            public Solution(string word, IEnumerable<Position> positions)
            {
                Word = word;
                Positions = positions;
            }
            protected override object[] GetEquatableValues()
            {
                return Positions.Cast<object>().ToArray().Append(Word).ToArray();
            }

            public override string ToString()
            {
                return $"{Word,-15} {Positions.First()}, {string.Join(" ", Position.CalculateDirections(Positions).Select(x => x.ToDirectionalString()))}";
            }

            internal bool IsStillValid(string[] grid)
            {
                for (int ii = 0; ii < Positions.Count(); ii++)
                {
                    if (Positions.ElementAt(ii).Get(grid) != Word[ii])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        internal class Position : EqualityBase<Position>
        {
            public static IEnumerable<Position> Directions { get; } = new Position[]
            {
                new Position(-1, -1),
                new Position(0, -1),
                new Position(1, -1),
                new Position(-1, 0),
                new Position(1, 0),
                new Position(-1, 1),
                new Position(0, 1),
                new Position(1, 1),
            };

            public static IEnumerable<Position> CalculateDirections(IEnumerable<Position> positions)
            {
                var result = new List<Position>();
                for (int ii = 1; ii < positions.Count(); ii++)
                {
                    result.Add(positions.ElementAt(ii) - positions.ElementAt(ii - 1));
                }
                return result;
            }

            public int X { get; }
            public int Y { get; }

            public Position(int x, int y)
            {
                X = x;
                Y = y;
            }

            public static Position operator +(Position a, Position b)
            {
                return new Position(a.X + b.X, a.Y + b.Y);
            }

            public static Position operator -(Position a, Position b)
            {
                return new Position(a.X - b.X, a.Y - b.Y);
            }

            public bool IsValid(string[] grid)
            {
                return X >= 0 && X < grid[0].Length && Y >= 0 && Y < grid.Length;
            }

            public char? Get(string[] grid)
            {
                if (!this.IsValid(grid))
                {
                    return null;
                }

                return grid[Y][X];
            }

            protected override object[] GetEquatableValues()
            {
                return new object[] { X, Y };
            }

            public string ToDirectionalString()
            {
                Debug.Assert(Directions.Contains(this));
                if (this.X == -1 && this.Y == -1)
                {
                    return "ul";
                }
                if (this.X == 0 && this.Y == -1)
                {
                    return "u";
                }
                if (this.X == 1 && this.Y == -1)
                {
                    return "ur";
                }
                if (this.X == -1 && this.Y == 0)
                {
                    return "l";
                }
                if (this.X == 1 && this.Y == 0)
                {
                    return "r";
                }
                if (this.X == -1 && this.Y == 1)
                {
                    return "dl";
                }
                if (this.X == 0 && this.Y == 1)
                {
                    return "d";
                }
                if (this.X == 1 && this.Y == 1)
                {
                    return "dr";
                }
                throw new InvalidOperationException();
            }

            public override string ToString()
            {
                return $"{this.X}, {this.Y}";
            }
        }
    }
}
