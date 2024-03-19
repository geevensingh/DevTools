// See https://aka.ms/new-console-template for more information


using System.Diagnostics;
using System.Text;
using Utilities.Extensions;

internal class Program
{
    private static async Task Main(string[] args)
    {
        HashSet<string> words = new HashSet<string>(await File.ReadAllLinesAsync("words_alpha.txt"));
        var parts = new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "A", "B", "C", "D", "E", "0" };
        for (int countToPick = 1; countToPick < 8; countToPick++)
        {
            for (int ii = 0; ii < 3; ii++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                var allParts = await parts.GetPermutationsAsync(countToPick);
                Console.WriteLine($"{sw.Elapsed.TotalSeconds:F} sec to pick {countToPick} from {parts.Length} which comes to {allParts.Count():N}");
            }
        }
        return;

        //foreach ( var part in allParts )
        //{
        //    string full = string.Join("", part).ToLower();
        //    string firstWord = full.Substring(0, 8);
        //    if (!words.Contains(firstWord)) { continue; }
        //    string secondWord = full.Substring(8, 7);
        //    if (!words.Contains(secondWord)) { continue; }
        //    //string thirdWord = full.Substring(5, 7);
        //    //if (!words.Contains(thirdWord)) { continue; }

        //    Console.WriteLine($"{firstWord} {secondWord}");
        //}
        //return;



        ////LongDivision longDivision = new LongDivision(641120, 397);
        //LongDivision longDivision = new LongDivision(7459031, 643);
        //Console.WriteLine($"answer : {longDivision.Answer}");
        //Console.WriteLine($"Full output : {longDivision}");

        ////string starter = "egg / hatching = llhugi + unt";
        ////string starter = "art / floods = ofol + afd";
        ////string starter = "spun / turntable = rtaas + tstu";
        ////string starter = "lowry / worcester = weel + yostw a";
        //string starter = "BARN / BETROTHAL = LAREON + HRB i".ToLower();
        //char[] letters = starter.ToCharArray().Distinct().Where(x => x >= 'a' && x <= 'z').ToArray();
        //if (letters.Length != 10)
        //{
        //    Console.WriteLine($"Not enough letters provided - only {letters.Length}");
        //    Console.WriteLine("Adding missing letters");
        //    Console.WriteLine(string.Join(",", letters.OrderBy(x => x)));
        //    return;
        //}

        //Debug.Assert(letters.Length == 10);
        //int index = 0;
        //string divisorString = starter.Split(" ")[index++];
        //Debug.Assert(starter.Split(" ")[index++] == "/");
        //string dividendString = starter.Split(" ")[index++];
        //Debug.Assert(starter.Split(" ")[index++] == "=");
        //string quotientString = starter.Split(" ")[index++];
        //Debug.Assert(starter.Split(" ")[index++] == "+");
        //string remainderString = starter.Split(" ")[index++];

        //var allDigits = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        //var allCombinations = GetPermutations(allDigits, allDigits.Length);

        //var possibleReplacements = new HashSet<char[]>();
        //var invalidPositionsForZero = new HashSet<int>();
        //foreach (char[] replacement in allCombinations)
        //{
        //    int indexOfZero = IndexOf(replacement, '0');
        //    if (invalidPositionsForZero.Contains(indexOfZero))
        //    {
        //        continue;
        //    }

        //    int possibleDivisor, possibleDividend, possibleQuotient;
        //    try
        //    {
        //        possibleDivisor = GetNumber(divisorString, letters, replacement);
        //        possibleDividend = GetNumber(dividendString, letters, replacement);
        //        possibleQuotient = GetNumber(quotientString, letters, replacement);
        //    }
        //    catch (ArgumentException ex) when (ex.Message == "First digit cannot be a zero")
        //    {
        //        invalidPositionsForZero.Add(indexOfZero);
        //        continue;
        //    }

        //    if (possibleDivisor == 0)
        //    {
        //        continue;
        //    }

        //    if (possibleDividend / possibleDivisor == possibleQuotient)
        //    {
        //        Console.WriteLine(string.Join(", ", replacement));
        //        Console.WriteLine(NumbersToLetters("0123456789", letters, replacement));
        //        possibleReplacements.Add(replacement);
        //    }
        //}

        //Console.WriteLine("----------------");
        //foreach (char[] replacement in possibleReplacements)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    sb.AppendLine(string.Join(", ", replacement));
        //    sb.AppendLine(string.Join(", ", letters));
        //    string extractedWord = NumbersToLetters("0123456789", letters, replacement);
        //    sb.AppendLine($"extracted word: {extractedWord}");
        //    longDivision = new LongDivision(GetNumber(dividendString, letters, replacement), GetNumber(divisorString, letters, replacement));
        //    sb.AppendLine($"answer : {longDivision.Answer} == {GetNumber(quotientString, letters, replacement)}");
        //    sb.AppendLine($"Full output as numbers : {longDivision}");
        //    string longDivisionAsLetters = NumbersToLetters(longDivision.ToString(), letters, replacement);
        //    sb.AppendLine($"Full output as letters : {longDivisionAsLetters}");
        //    string[] lines = longDivisionAsLetters.Split("\r\n");
        //    if (lines.Last(x => !string.IsNullOrEmpty(x)) == remainderString)
        //    {
        //        Console.Write(sb.ToString());
        //    }
        //    //Console.Write(sb.ToString());
        //}
    }

    static int IndexOf<T>(T[] array, T item)
    {
        for (int ii = 0; ii < array.Length; ii++)
        {
            if (array[ii].Equals(item))
            {
                return ii;
            }
        }

        return -1;
    }

    static string NumbersToLetters(string stringOfNumbers, char[] uniqueChars, char[] combination)
    {
        string result = stringOfNumbers;
        for (int ii = 0; ii < uniqueChars.Length; ii++)
        {
            result = result.Replace(combination[ii], uniqueChars[ii]);
        }

        return result;
    }

    static string LettersToNumbers(string numAsString, char[] uniqueChars, char[] combination)
    {
        string result = numAsString;
        for (int ii = 0; ii < uniqueChars.Length; ii++)
        {
            result = result.Replace(uniqueChars[ii], combination[ii]);
        }

        if (result[0] == '0')
        {
            throw new ArgumentException("First digit cannot be a zero");
        }

        return result;
    }

    static int GetNumber(string numAsString, char[] uniqueChars, char[] combination)
    {
        return int.Parse(LettersToNumbers(numAsString, uniqueChars, combination));
    }
}