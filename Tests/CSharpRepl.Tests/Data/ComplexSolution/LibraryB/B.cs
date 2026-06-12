using System;
using Newtonsoft.Json;

namespace LibraryB
{
    public static class B
    {
        public static void SayHello() =>
            Console.WriteLine(
                JsonConvert.DeserializeObject("\"Hello from Library B\"")
            );
    }
}
