These are good days for optimization on the dotnet landscape.
 
New knobs and whistles appear every day on the ecosystem, and with that the "need" to optimize old stuff arrives.

As I was modernizing one of VTEX's internal libraries, I came across a typical piece of code that can benefit from the new stuff
that has been introduced in the ecosystem, the code is simple and I think quite wide spread: 

```csharp
public class MD5Helper
{
    public static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        using (var md5 = MD5.Create())
            return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}
```
A hashing helper function that was on a hot path. So with this as a starting point, let's begin applying some techniques to make this code a bit more efficient. 

First we can see where this code is allocating extra memory:

- `byte[] Encoding.UTF8.GetBytes(string)` Allocattes the array of bytes of the utf8 representation of the string.
- `MD5 MD5.Create()` Allocates the hash algorithm.
- `byte[] MD5.ComputeHash(bytes)` Allocates another array of bytes representing the actual hash.
- `string BitConverter.ToString(byte[])` Allocates the hex string reprentation of the byte array.
- `string String.Replace(string, string)` Allocates another string without the "-" for a more compact representation of the hash.
- `string String.ToLowerInvariant()` Allocates another string but in lower case.

Ideally we would like to allocate only the actual hash of the string.

The first thing that catches my eye is `String.Replace(string, string)` and `String.ToLowerInvariant()`, allocating 2 extra strings seems excessive.

Since dotnet code is open source, we can take a look at what `BitConverter.ToString(byte[])` is doing:

```csharp
const string HexValues = "0123456789ABCDEF";

var src = new ReadOnlySpan<byte>(state.value, state.startIndex, state.length);
 
int i = 0;
int j = 0;
 
byte b = src[i++];
dst[j++] = HexValues[b >> 4];
dst[j++] = HexValues[b & 0xF];
 
while (i < src.Length)
{
    b = src[i++];
    dst[j++] = '-';
    dst[j++] = HexValues[b >> 4];
    dst[j++] = HexValues[b & 0xF];
}
```

Simple enough, we can tweak this to give us the output we need. Going from:

```csharp
BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()
```

To:

```csharp
private static string ToHex(byte[] bytes)
{
    const string alphabet = "0123456789abcdef";

    var c = new char[bytes.Length * 2];

    var i = 0;
    var j = 0;

    while (i < bytes.Length)
    {
        var b = bytes[i++];
        c[j++] = alphabet[b >> 4];
        c[j++] = alphabet[b & 0xF];
    }

    return new string(c);
}
```
We are allocating an extra `char[]`, but more on this later.

We should test each iteration of the optimization process to be sure we are actually gaining something, for this I used the super cool BenchmarkDotNet:
```csharp
[MemoryDiagnoser]
public class MD5HashBenchmarks
{
    public const string Content = @"Lorem ipsum dolor sit amet, consectetur adipiscing elit. 
Vivamus aliquam et augue in vehicula. Fusce tempus libero et lacus auctor tempor. Donec convallis 
eros sit amet justo hendrerit, et mollis nisi bibendum. Sed eu nisi ac mi luctus varius ac in velit. 
In hac habitasse platea dictumst. Nam tincidunt sem sapien, id rhoncus est tincidunt ut. Quisque 
sapien nulla, dignissim eget mollis ac, tempus sit amet lorem. Vivamus quam ipsum, malesuada eget 
turpis non, interdum eleifend massa. Maecenas vel mi volutpat, cursus ligula ac, tincidunt sapien. 
Maecenas gravida lectus massa, vel auctor ante fringilla vitae. Duis ultrices mi nec tellus facilisis 
ullamcorper. Proin vitae cursus nunc, id hendrerit purus. Integer id magna in dui sodales iaculis sed 
eu dui.";

    [Benchmark(Baseline = true)]
    public string Original() => MD5Helper.ComputeHash(Content);

    [Benchmark]
    public string Step1() => MD5HelperStep1.ComputeHash(Content);
}
```

And the results:

|   Method |     Mean |     Error |    StdDev | Scaled |  Gen 0 | Allocated |
|--------- |---------:|----------:|----------:|-------:|-------:|----------:|
| Original | 2.876 us | 0.0229 us | 0.0214 us |   1.00 | 0.2594 |    1232 B |
|    Step1 | 2.574 us | 0.0158 us | 0.0140 us |   0.90 | 0.2518 |    1200 B |

So we got a nice increase in performance and a bit less memory allocated.

Now, this extra char[] allocation on the new `ToHex` method can be eliminated using **object pooling**.

**Object pooling** is basicaly manual memory management, where we rent a object, use it, and then return it to the pool so it can be reused later. We can use it to decrease pressure on the Garbage Collector.

We can use the utility class `ArrayPool<T>` that resides in the `System.Buffers` namespace and rewrite the method:

```csharp
private static readonly ArrayPool<char> CharArrayPool = ArrayPool<char>.Shared;

private static string ToHex(byte[] bytes)
{
    const string alphabet = "0123456789abcdef";

    var length = bytes.Length * 2;
    var buffer = CharArrayPool.Rent(length); // this will return an array that is garanteed to be 
                                             // AT LEAST the length we asked, but it can be bigger 
					     // (it will almost always be the case).

    var c = buffer.AsSpan(0, length); // since it can be bigger,
                                      // we slice the array to the size we actualy want.

    var i = 0;
    var j = 0;

    while (i < bytes.Length)
    {
        var b = bytes[i++];
        c[j++] = alphabet[b >> 4];
        c[j++] = alphabet[b & 0xF];
    }
            
    var result = new string(c); //we use the "new" constructor of string that takes a Span<char>.

    CharArrayPool.Return(buffer); //finally, we return the buffer to the pool.

    return result;
}
```

Let's add this case and run the benchmark again:

|   Method |     Mean |     Error |    StdDev | Scaled |  Gen 0 | Allocated |
|--------- |---------:|----------:|----------:|-------:|-------:|----------:|
| Original | 2.876 us | 0.0229 us | 0.0214 us |   1.00 | 0.2594 |    1232 B |
|    Step1 | 2.574 us | 0.0158 us | 0.0140 us |   0.90 | 0.2518 |    1200 B |
|    Step2 | 2.664 us | 0.0208 us | 0.0184 us |   0.93 | 0.2327 |    1112 B |

The pooling introduced some overhead, but the allocated memory went down again, for now I am happy with the trade-off.

Next we are going to attack the `byte[]` that  `md5.ComputeHash(bytes)` is allocating. Once again, when checking other available apis, one seems to be just what we want:

```csharp
bool TryComputeHash(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
```

This overload receives a buffer instead of allocating one itself. The api hints that it may fail, and that it can allocate a variable amount of bytes.

But checking the source code, the `MD5` class is a `HashAlgorithm`, which is a poorly designed api, the `MD5` implementation will only fail if we pass it a buffer with not enough capacity, and the bytesWritten is fixed, in this case, 16 bytes.

With this information in mind, we can now rewrite our `ComputeHash` to use a `byte[]` from the pool:

```csharp
private static readonly ArrayPool<byte> ByteArrayPool = ArrayPool<byte>.Shared;

public static string ComputeHash(string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);

    using (var md5 = MD5.Create())
    {
        const int hashSizeInBytes = 16;

        var buffer = ByteArrayPool.Rent(hashSizeInBytes);

        var hash = buffer.AsSpan(0, hashSizeInBytes);
                
        md5.TryComputeHash(bytes, hash, out _);

        var result = ToHex(hash);

        ByteArrayPool.Return(buffer);

        return result;
    }
}
```

|   Method |     Mean |     Error |    StdDev | Scaled |  Gen 0 | Allocated |
|--------- |---------:|----------:|----------:|-------:|-------:|----------:|
| Original | 2.876 us | 0.0229 us | 0.0214 us |   1.00 | 0.2594 |    1232 B |
|    Step1 | 2.574 us | 0.0158 us | 0.0140 us |   0.90 | 0.2518 |    1200 B |
|    Step2 | 2.664 us | 0.0208 us | 0.0184 us |   0.93 | 0.2327 |    1112 B |
|    Step3 | 2.570 us | 0.0235 us | 0.0208 us |   0.89 | 0.2174 |    1032 B |

Again a little bit more performance, and a little drop on memory usage.

Now we can attack one big outlier on the allocation side: `Encoding.UTF8.GetBytes(string)`

This one will allocate as much memory as the input string: the bigger the string, the bigger the `byte[]`.

Again, there is another api that we can use:

```csharp
int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
```

Like `MD5.TryComputeHash`, it receives the output buffer instead of allocating one on its own, but chars in utf8 can require from 1 up to 4 bytes per char, so to get our buffer from the pool, we need to ask the encoder how many bytes it will need, fortunately there is an api for that:

```csharp
int GetByteCount(string s);
```

With this, we now have everything we need to rewrite our method using pooled `byte[]` for the `Encoding.UTF8.GetBytes`:

```csharp
public static string ComputeHash(string value)
{
    var length = Encoding.UTF8.GetByteCount(value);
    var encoderBuffer = ByteArrayPool.Rent(length);

    Encoding.UTF8.GetBytes(value, encoderBuffer);

    using (var md5 = MD5.Create())
    {
        const int hashSizeInBytes = 16;

        var buffer = ByteArrayPool.Rent(hashSizeInBytes);

        var hash = buffer.AsSpan(0, hashSizeInBytes);

        md5.TryComputeHash(encoderBuffer.AsSpan(0, length), hash, out _);

        var result = ToHex(hash);

        ByteArrayPool.Return(buffer);
        ByteArrayPool.Return(encoderBuffer);

        return result;
    }
}
```

|   Method |     Mean |     Error |    StdDev | Scaled |  Gen 0 | Allocated |
|--------- |---------:|----------:|----------:|-------:|-------:|----------:|
| Original | 2.876 us | 0.0229 us | 0.0214 us |   1.00 | 0.2594 |    1232 B |
|    Step1 | 2.574 us | 0.0158 us | 0.0140 us |   0.90 | 0.2518 |    1200 B |
|    Step2 | 2.664 us | 0.0208 us | 0.0184 us |   0.93 | 0.2327 |    1112 B |
|    Step3 | 2.570 us | 0.0235 us | 0.0208 us |   0.89 | 0.2174 |    1032 B |
|    Step4 | 2.555 us | 0.0144 us | 0.0120 us |   0.89 | 0.0458 |     224 B |

Wow! About the same performance, but the memory allocations went way down! Still we have more stuff to do.

So far we did everything that is obviously "safe", now we can start on stuff where it might not be so obvious.

There is still one thing that is being allocated: `MD5.Create()`. Since its IDisposable, we are creating and disposing of one in each method call. 

But, remember that I told you that this is a poor abstraction? The same applies here to the disposable pattern: while it holds unmanaged resources that should be disposed, we don't necessarily need to create and dispose of one on each method call.

Taking a look at the implementation we see that IsReusable from `HashAlgorithm` is True, a quick test confirms that we can hash multiple items without any problems. But the implementation is still not thread safe, so we can't share one instance of MD5 across threads.

We can use the `ThreadLocal<T>` class to guarantee a minimal quantity of instances, and also that we won't access the MD5 concurrently.

With all this information at hand, we now can do this:

```csharp
private static readonly ThreadLocal<MD5> Hasher = new ThreadLocal<MD5>(MD5.Create);

public static string ComputeHash(string value)
{
    var length = Encoding.UTF8.GetByteCount(value);
    var encoderBuffer = ByteArrayPool.Rent(length);

    Encoding.UTF8.GetBytes(value, encoderBuffer);

    const int hashSizeInBytes = 16;

    var buffer = ByteArrayPool.Rent(hashSizeInBytes);

    var hash = buffer.AsSpan(0, hashSizeInBytes);

    Hasher.Value.TryComputeHash(encoderBuffer.AsSpan(0, length), hash, out _);

    var result = ToHex(hash);

    ByteArrayPool.Return(buffer);
    ByteArrayPool.Return(encoderBuffer);

    return result;
}
```

|   Method |     Mean |     Error |    StdDev | Scaled |  Gen 0 | Allocated |
|--------- |---------:|----------:|----------:|-------:|-------:|----------:|
| Original | 2.876 us | 0.0229 us | 0.0214 us |   1.00 | 0.2594 |    1232 B |
|    Step1 | 2.574 us | 0.0158 us | 0.0140 us |   0.90 | 0.2518 |    1200 B |
|    Step2 | 2.664 us | 0.0208 us | 0.0184 us |   0.93 | 0.2327 |    1112 B |
|    Step3 | 2.570 us | 0.0235 us | 0.0208 us |   0.89 | 0.2174 |    1032 B |
|    Step4 | 2.555 us | 0.0144 us | 0.0120 us |   0.89 | 0.0458 |     224 B |
|    Step5 | 2.124 us | 0.0146 us | 0.0122 us |   0.74 | 0.0191 |      96 B |

Yes! Now not only are we just allocating the fixed size hash string, but we also got a boost in performance for not constantly calling `MD5.Create` and `Dispose`.

Another recent thing that C# got is stackalloc-ing to Spans, with this we can stackalloc without messing with pointers. 

Stackalloc-ed arrays can be dangerous since we have limited stack space (1MB the last time I checked), but they are faster to allocate and won't create garbage, since the memory is released when the variable is out of scope.

We can apply this here to 2 small and constant size arrays:

```csharp
public static string ComputeHash(string value)
{
    var length = Encoding.UTF8.GetByteCount(value);
    var encoderBuffer = ByteArrayPool.Rent(length);

    Encoding.UTF8.GetBytes(value, encoderBuffer);

    Span<byte> hash = stackalloc byte[16]; // <-- here

    Hasher.Value.TryComputeHash(encoderBuffer.AsSpan(0, length), hash, out _);

    var result = ToHex(hash);
           
    ByteArrayPool.Return(encoderBuffer);

    return result;
}

private static string ToHex(in ReadOnlySpan<byte> bytes)
{
    const string alphabet = "0123456789abcdef";

    Span<char> c = stackalloc char[32]; // <-- here

    var i = 0;
    var j = 0;

    while (i < bytes.Length)
    {
        var b = bytes[i++];
        c[j++] = alphabet[b >> 4];
        c[j++] = alphabet[b & 0xF];
    }

    var result = new string(c);

    return result;
}
```

|   Method |     Mean |     Error |    StdDev | Scaled |  Gen 0 | Allocated |
|--------- |---------:|----------:|----------:|-------:|-------:|----------:|
| Original | 2.876 us | 0.0229 us | 0.0214 us |   1.00 | 0.2594 |    1232 B |
|    Step1 | 2.574 us | 0.0158 us | 0.0140 us |   0.90 | 0.2518 |    1200 B |
|    Step2 | 2.664 us | 0.0208 us | 0.0184 us |   0.93 | 0.2327 |    1112 B |
|    Step3 | 2.570 us | 0.0235 us | 0.0208 us |   0.89 | 0.2174 |    1032 B |
|    Step4 | 2.555 us | 0.0144 us | 0.0120 us |   0.89 | 0.0458 |     224 B |
|    Step5 | 2.124 us | 0.0146 us | 0.0122 us |   0.74 | 0.0191 |      96 B |
|    Step6 | 2.083 us | 0.0099 us | 0.0083 us |   0.72 | 0.0191 |      96 B |

This gave us a small performance boost, and a nice cleanup of the code.

We are only using the `ArrayPool<T>` on the variable size buffer for the `Encoding.UTF8.GetBytes`.

I was about to finish here: we are doing things faster and with a nice decrease on memory allocation. But my friend **Lima The Barbarian** pointed out that we could overprovision the utf8 encoding buffer, so we would not need to parse twice to know the amount of bytes the encoder would take:

```csharp
public static string ComputeHash(string value)
{
    var buffer = ByteArrayPool.Rent(Encoding.UTF8.GetMaxByteCount(value.Length)); //here we can get the maximum possible size of the encoded chars.

    var length = Encoding.UTF8.GetBytes(value, buffer); //the api will return the amount of bytes read.

    Span<byte> hash = stackalloc byte[16];

    Hasher.Value.TryComputeHash(buffer.AsSpan(0, length), hash, out _);

    var result = ToHex(hash);
           
    ByteArrayPool.Return(buffer);

    return result;
}
```

|   Method |     Mean |     Error |    StdDev | Scaled |  Gen 0 | Allocated |
|--------- |---------:|----------:|----------:|-------:|-------:|----------:|
| Original | 2.876 us | 0.0229 us | 0.0214 us |   1.00 | 0.2594 |    1232 B |
|    Step1 | 2.574 us | 0.0158 us | 0.0140 us |   0.90 | 0.2518 |    1200 B |
|    Step2 | 2.664 us | 0.0208 us | 0.0184 us |   0.93 | 0.2327 |    1112 B |
|    Step3 | 2.570 us | 0.0235 us | 0.0208 us |   0.89 | 0.2174 |    1032 B |
|    Step4 | 2.555 us | 0.0144 us | 0.0120 us |   0.89 | 0.0458 |     224 B |
|    Step5 | 2.124 us | 0.0146 us | 0.0122 us |   0.74 | 0.0191 |      96 B |
|    Step6 | 2.083 us | 0.0099 us | 0.0083 us |   0.72 | 0.0191 |      96 B |
|    Step7 | 1.934 us | 0.0120 us | 0.0106 us |   0.67 | 0.0191 |      96 B |

With this we get another little boost in performance!

So in the end we have:

|   Method |     Mean |     Error |    StdDev | Scaled |  Gen 0 | Allocated |
|--------- |---------:|----------:|----------:|-------:|-------:|----------:|
| Original | 2.876 us | 0.0229 us | 0.0214 us |   1.00 | 0.2594 |    1232 B |
|    Final | 1.934 us | 0.0120 us | 0.0106 us |   0.67 | 0.0191 |      96 B |

We got 33% faster execution and a 92.2% decrease in memory usage by using the new apis!