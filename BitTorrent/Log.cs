using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BitTorrent
{
    public static class Log
    {
        public static void Write(string output)
        {
            Console.Write(DateTime.UtcNow.ToString("hh:mm:ss.fff") + "|" + Thread.CurrentThread.ManagedThreadId.ToString().PadLeft(5, '0') + ": " + output);
        }

        public static void WriteLine(object output)
        {
            Write(output + "\n");
        }

        public static void WriteLine(object obj, string output)
        {
            Write(obj + " " + output + "\n");
        }
    }
}
