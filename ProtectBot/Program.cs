using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProtectBot
{
    class Program
    {
        static void Main(string[] args)
        {
            BinanceFuturesRest bfr = new BinanceFuturesRest();
            bfr.Start();

            Thread.Sleep(2000);
        }
    }
}
