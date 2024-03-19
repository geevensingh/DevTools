// See https://aka.ms/new-console-template for more information


using LetterLongDivision;
using System.Diagnostics;

internal class LongDivision
{
    private int dividend;
    private int divisor;
    private LongDivision? next = null;

    public LongDivision(int dividend, int divisor)
    {
        this.dividend = dividend;
        this.divisor = divisor;
        this.Divide();
    }

    public int Result => this.dividend / this.divisor;
    public int Remainder => this.dividend % this.divisor;
    public string Answer => $"{this.Result}, r {this.Remainder}";

    private void Divide()
    {
        if (this.dividend < this.divisor)
        {
            return;
        }

        int power = (int)Math.Floor(Math.Log10(this.dividend / this.divisor));
        int firstDigit = this.dividend / this.divisor / (int)Math.Pow(10, power);
        Debug.Assert(firstDigit > 0);
        Debug.Assert(firstDigit < 10);
        int result = firstDigit;

        int firstRow = firstDigit * this.divisor;
        int dividendPrefixLength = this.dividend.Length() - power;
        int secondRow = this.dividend.Start(dividendPrefixLength) - firstRow;
        int secondRowAdjusted = this.GetRest(secondRow, dividendPrefixLength);

        if (secondRowAdjusted != secondRow)
        {
            this.next = new LongDivision(secondRowAdjusted, this.divisor);
            result = this.next.Result + (firstDigit * (int)Math.Pow(10, power));
            Debug.Assert(this.Result == result);
            Debug.Assert(this.Remainder == this.next.Remainder);
        }
        else
        {
            Debug.Assert(this.Result == result);
            Debug.Assert(this.Remainder == secondRow);
        }
    }

    private int DropDown(int row, int length)
    {
        return row * 10 + this.dividend.GetDigit(length);
    }

    public int GetRest(int row, int toSkip)
    {
        string dividendString = this.dividend.ToString();
        string restOfDividend = dividendString.Substring(toSkip);
        if (string.IsNullOrEmpty(restOfDividend))
        {
            return row;
        }
        return int.Parse($"{row}{restOfDividend}");
    }

    public override string ToString()
    {
        string result = $"  {this.Answer}\r\n" +
            $"{this.divisor} % {this.dividend}\r\n" + 
            $"{this.GetSubstring()}\r\n";
        return result;
    }

    private string GetSubstring()
    {
        int subtractor = this.Result.GetDigit(0) * this.divisor;
        string result = $"{this.dividend.Start(subtractor.Length())}\r\n{subtractor}\r\n";
        if (this.next != null)
        {
            result += this.next.GetSubstring();
        }
        else
        {
            result += this.Remainder;
        }

        return result;
    }
}