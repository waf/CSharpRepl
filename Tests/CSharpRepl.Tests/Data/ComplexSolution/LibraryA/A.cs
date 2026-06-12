using System;
using LibraryB;

namespace LibraryA
{
    public class A
    {
        public static void SayHello()
        {
            Console.WriteLine("Hello from Library A");
            B.SayHello();
        }
    }
}
