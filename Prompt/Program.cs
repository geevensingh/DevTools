using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utilities;

namespace Prompt
{
    class Program
    {
        static void Main(string[] args)
        {
            Logger.Log(GitOperations.GetCurrentBranchName());
            Logger.Log(GitOperations.GetFileStatus());
        }
    }
}
