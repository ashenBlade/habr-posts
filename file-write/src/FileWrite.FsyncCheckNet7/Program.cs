using var file = new FileStream("sample.txt", FileMode.OpenOrCreate);
file.Write("hello, world"u8);
file.Flush(true);