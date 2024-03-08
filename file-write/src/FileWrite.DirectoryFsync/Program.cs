using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

var directory = Directory.CreateDirectory("sample-directory");
const int directoryFlags = 65536; // O_DIRECTORY | O_RDONLY
var handle = Open(directory.FullName, directoryFlags); 
using var stream = new FileStream(new SafeFileHandle(handle, true), FileAccess.ReadWrite);
stream.Flush(true);

[DllImport("libc", EntryPoint = "open")]
static extern nint Open(string path, int flags);