using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace AnOptimizationHistory
{
    [MemoryDiagnoser]
    public class MD5HashBenchmarks
    {
        public const string CONTENT = @"Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Vivamus aliquam et augue in vehicula. Fusce tempus libero et lacus auctor tempor. Donec convallis 
eros sit amet justo hendrerit, et mollis nisi bibendum. Sed eu nisi ac mi luctus varius ac in velit. 
In hac habitasse platea dictumst. Nam tincidunt sem sapien, id rhoncus est tincidunt ut. Quisque 
sapien nulla, dignissim eget mollis ac, tempus sit amet lorem. Vivamus quam ipsum, malesuada eget 
turpis non, interdum eleifend massa. Maecenas vel mi volutpat, cursus ligula ac, tincidunt sapien. 
Maecenas gravida lectus massa, vel auctor ante fringilla vitae. Duis ultrices mi nec tellus facilisis 
ullamcorper. Proin vitae cursus nunc, id hendrerit purus. Integer id magna in dui sodales iaculis sed 
eu dui.";

        [Benchmark(Baseline = true)]
        public string Original() => MD5Helper.ComputeHash(CONTENT);

        [Benchmark]
        public string Step1() => MD5HelperStep1.ComputeHash(CONTENT);

        [Benchmark]
        public string Step2() => MD5HelperStep2.ComputeHash(CONTENT);

        [Benchmark]
        public string Step3() => MD5HelperStep3.ComputeHash(CONTENT);

        [Benchmark]
        public string Step4() => MD5HelperStep4.ComputeHash(CONTENT);

        [Benchmark]
        public string Step5() => MD5HelperStep5.ComputeHash(CONTENT);

        [Benchmark]
        public string Step6() => MD5HelperStep6.ComputeHash(CONTENT);

        [Benchmark]
        public string Step7() => MD5HelperStep7.ComputeHash(CONTENT);
    }

    internal class Program
    {
        private static void Main()
        {
            BenchmarkRunner.Run<MD5HashBenchmarks>();
        }
    }
}
