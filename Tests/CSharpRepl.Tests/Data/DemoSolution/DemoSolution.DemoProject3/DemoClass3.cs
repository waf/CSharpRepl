namespace DemoSolution.DemoProject3
{
    public static class DemoClass3
    {
        public static void Main() { }

        public static string GetSystemManagementPath()
            => typeof(System.Management.ConnectionOptions).Assembly.Location;
    }
}