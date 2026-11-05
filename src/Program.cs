// Entry point for the standalone .exe build (the .bat calls Launcher.Run directly).
using System;

namespace TaskbarTamer
{
    static class Program
    {
        [STAThread]
        static void Main() { Launcher.Run(); }
    }
}
