using System;
using System.IO;
using System.Threading;
using dnlib.DotNet;
using MirrorUnweavers;
using dnlib.DotNet.Writer;

namespace MirrorUnweaver
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Mirror Unweaver Engine v1.1";

            if (args.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("[-] Error: Please specify the path to Assembly-CSharp.dll");
                Console.ResetColor();
                return;
            }

            string inputPath = args[0];
            string outputPath = Path.Combine(Path.GetDirectoryName(inputPath), Path.GetFileNameWithoutExtension(inputPath) + "_Unweaved.dll");

            if (!File.Exists(inputPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] Error: File not found at {inputPath}");
                Console.ResetColor();
                Console.ReadKey();
                return;
            }

            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[*] Loading assembly...");
                ModuleDefMD module = ModuleDefMD.Load(inputPath);
                Console.WriteLine($"[!] Target: {module.Name}");
                Console.ResetColor();

                var unweaver = new UnweaverEngine(module);

                Console.WriteLine("\n[*] Starting Deep Cleaning Process:");

                string[] steps = ["Restoring User Code", "Marking Mirror Garbage", "Cleaning IL Instructions", "Finalizing Removal"];
                for (int i = 0; i < steps.Length; i++)
                {
                    DrawProgressBar(i + 1, steps.Length, 30, steps[i]);

                    if (i == 0) unweaver.Run();
                    Thread.Sleep(300); 
                }

                Console.WriteLine("\n\n[*] Writing patched assembly to disk...");
                var options = new ModuleWriterOptions(module)
                {
                    MetadataLogger = DummyLogger.NoThrowInstance
                };

                module.Write(outputPath, options);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[+++] SUCCESS! Cleaned assembly saved to:");
                Console.WriteLine($">>> {outputPath}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[-] CRITICAL ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        static void DrawProgressBar(int progress, int total, int width, string taskName)
        {
            Console.CursorLeft = 0;
            Console.Write("[");

            int filledWidth = (progress * width) / total;

            Console.ForegroundColor = ConsoleColor.Green;
            for (int i = 0; i < filledWidth; i++) Console.Write("#");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            for (int i = filledWidth; i < width; i++) Console.Write("-");

            Console.ResetColor();
            Console.Write($"] {progress * 100 / total}% | Current Task: {taskName,-25}");
        }
    }
}