using System.Reflection;
using Xunit;

namespace MoveSense.Core.Tests;

// The same tests can run without VSTest's loopback socket in restricted environments.
internal static class Program
{
    public static int Main()
    {
        var failed = 0;
        var passed = 0;
        foreach (var type in typeof(Program).Assembly.GetTypes())
        foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() is not null))
        {
            object? test = null;
            try
            {
                test = Activator.CreateInstance(type);
                method.Invoke(test, null);
                Console.WriteLine("PASS " + method.Name);
                passed++;
            }
            catch (Exception error)
            { Console.WriteLine("FAIL " + method.Name + ": " + (error.InnerException ?? error)); failed++; }
            finally { (test as IDisposable)?.Dispose(); }
        }
        Console.WriteLine($"{passed} passed; {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}

