Greetings!

Fault tolerance is a very important aspect of every non-startup application.
It can be described as a [definition](https://en.wikipedia.org/wiki/Fault_tolerance):

> Fault tolerance is the ability of a system to maintain proper operation despite failures or faults in one or more of its components.

But this gives only slight overview - fault tolerance concerns many areas especially when we are talking about software engineering:

- Network failures (i.e. connection halt due to power failure on intermediate router)
- Dependent service unavailability (i.e. another microservice)
- Hardware bugs (i.e. [Pentium FDIV bug](https://en.wikipedia.org/wiki/Pentium_FDIV_bug))
- Storage layer corruptions (i.e. [bit rot](https://en.wikipedia.org/wiki/Data_degradation))

As a database developer I'm interested in latter - in the end all data stored in disks.

But you should know that not only disks can lead to such faults. There are other pieces that can misbehave or it's developer's mistake that he/she didn't work with these parts correctly.

I'm going to explain how this 'file write stack' (named in opposite to 'network stack') works.
Of course, main concern will be about fault tolerance.

## Application

Everything starts in application's code. Usually, there is separate interface to work with files.

Each PL (programming language) has own interface. Some examples:

- `fwrite` - C
- `std::fstream.write` - C++
- `FileStream.Write` - C\#
- `FileOutputStream.Write` - Java
- `open().write` - Python
- `os.WriteFile` - go

These all means given by PL itself: read, write, etc...
Their main advantage - platform independence: runtime (i.e. C\#) or compiler (i.e. C) implements this.
But this have drawbacks. Buffering in this case - all calls to this virtual "write" function store going to be written data in special buffer to later write it all at once (make syscall to write).

Due to documentation, each of all PL above support buffering:

- C - [setvbuf](https://en.cppreference.com/w/c/io/setvbuf)
- C++ - [filebuf](https://cplusplus.com/reference/fstream/filebuf/)
- C# - [BufferedFileStrategy](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/BufferedFileStreamStrategy.cs)
- Java - [Files.newBufferedReader](https://docs.oracle.com/javase/8/docs/api/java/nio/file/Files.html#newBufferedWriter-java.nio.file.Path-java.nio.charset.Charset-java.nio.file.OpenOption...-)
- Python - [io.BufferedIOBase](https://docs.python.org/3/library/io.html#io.BufferedIOBase)
- go - [bufio.Reader](https://pkg.go.dev/bufio#Reader)

> What about C\# - class `FileStream` uses another class `FileStreamStrategy` - it handles all requests ([Strategy pattern](https://en.wikipedia.org/wiki/Strategy_pattern)).
> For example, when we are instantiating `FileStream` through `File.Open()` - `BufferedFileStrategy` wraps `OSFileStreamStrategy`.
> It adds buffering layer above syscalls.

Generaly, buffering in user space is a good feature - usually it improves performance.
But programmer should be aware of additional buffers. There are 2 types of buffering I can identify:

1. Manual (go, Java) - create buffered file object manually
2. Transparent (C, C++) - buffer maintained automatically

In first case we know, that buffer should be flushed at end of write session, but in second case can you can easily shoot in your foot:

1. File opened in memory, but no callbacks for flushing registered or just forgot to add such code
2. Application closed abnormally (i.e. got SIGKILL)

All such cases ends up with all data in in-memory buffer get lost.

So, there are 2 solutions:

1. Always flush data after write session.
   The easiest case for simple application is by adding single write function that accepts batch of write buffers and calls flush function at the end.
2. Disable buffering at all and make writes directly.

From performance view - first case is the most attractive.

{% details Performance comparison %}

I have made a small benchmark - sequential write of 64Mb data into a file.
Test matrix: old laptop with HDD and new laptop with SSD NVMe M.2.

|     | Direct write, s | Buffered write, s |
| --- | --------------- | ----------------- |
| Old | 894.7           | 109.6             |
| New | 8.932           | 1.198             |

The result is obvious: buffered IO faster by at least 8 times.
Source code of benchmark [here](https://github.com/ashenBlade/habr-posts/blob/file-write/file-write/src/FileWrite.Benchmarks/FileWriteBenchmarks.cs).

{% enddetails %}

## Operating system

Programming language gives good platform abstraction - programmer does not need to think about (at least, not very often) on which operating system application works.
But eventually, PL interface will be mapped to some syscalls.
These syscalls are OS dependent, but in common we have 4 main functions:

| Operation   | \*nix            | Windows      |
| ----------- | ---------------- | ------------ |
| Open/Create | `open`/`creat`   | `CreateFile` |
| Read        | `pread`/`read`   | `ReadFile`   |
| Write       | `pwrite`/`write` | `WriteFile`  |
| Close       | `close`          | `CloseFile`  |

> `*nix` means Unix-like OS: Linux, FreeBSD, OSX.
> Semantics of some operations can differ, but now it is not very important - common interface/behaviour pattern the same.

There is buffering also at OS layer. It is called [page buffer](https://en.wikipedia.org/wiki/Page_cache).
And it's even easier to shoot yourself in the foot with it.

Operations with file are perform in pages (huge sequential block of data, usually 4Kb) even if only single byte was requested.

When we read page is stored in buffer. Then we send write request that is not written to device immeately. Instead, it just marked as "dirty" and scheduled to be flushed at near future.
All such pages (clean and dirty) make up a page cache.

How it can harm us? Follow these steps:

1. User sends request to update info about himself. For example, it can be his password
2. We open file with users information - retrieve required page
3. Page is updated with new password (of course it will be password hash for security reasons)
4. Application make "write" request to OS (everything is ok)
5. We tell user that everything is updated

What could go wrong? Power outage!

1. User replaced his old password with new one in his password manager (old *cryptographic* password)
2. "dirty" page with new password (step 3) was lost because of power failure and old password still the same (on disk)

Congratulations - user lost access to his account.

Page cache is very helpfull, especially when there are many operations on same page.
But when we should make a "durable" write (that is it must be saved to disk permanently, not being just updated in memory) we must ensure data is flushed to disk.

Even here there are 2 solutions:

1. Special syscall to flush data to disk
2. Bypass page cache and always write to disk

### Special syscall to flush data

This solution uses separate syscall that ensures that all our write requests were completed.

#### Linux

In Linux we can use:

1. `fdatasync(fd)` - checks, that *data in memory* and *on disk* are synchronized, that is performs flushing of pending ("dirty") pages.
2. `fsync(fd)` - the same as `fdatasync(fd)`, but also updates file metadata (i.e. last access time). Usually, you don't need this and should prefer `fdatasync` for performance.
3. `sync_file_range(fd, range)` - checks that specific range of file is flushed to disk (according to `man` this is very dangerous function, so I added it here just for familiarization)
4. `sync()` - it flushes all pages to disk, but for *all files*, not some specified


As said earlier, `fdatasync` synchronizes only contents, without metadata like `fsync`, so it is faster.
etcd [comments this out](https://github.com/etcd-io/etcd/blob/6f55dfa26e1a359e47e1fb15af79951e97dbac39/client/pkg/fileutil/sync_linux.go#L32) and makes explicit separation between them:

```go
// Fsync is a wrapper around file.Sync(). Special handling is needed on darwin platform.
func Fsync(f *os.File) error {
	return f.Sync()
}

// Fdatasync is similar to fsync(), but does not flush modified metadata
// unless that metadata is needed in order to allow a subsequent data retrieval
// to be correctly handled.
func Fdatasync(f *os.File) error {
	return syscall.Fdatasync(int(f.Fd()))
}
```

#### Windows

For Windows there are functions:

- `_commit` - flushes data from file to disk
- `FlushFileBuffers(fd)` - causes write all buffered data to a file (I'm not a Windows developer, so do not known difference with previous one)
- `NtFlushBuffersFileEx(fd, params)` - causes page cache flush, but it is more low-level function (marked with `NTSYSCALLAPI`)

P.S. `NtFlushBuffersFileEx` [used in Postgres](https://github.com/postgres/postgres/blob/874d817baa160ca7e68bee6ccc9fc1848c56e750/src/port/win32fdatasync.c#L40) as a writer for crossplatform `fdatasync`, and for `fsync` it uses `_commit`.

```cpp
// https://github.com/postgres/postgres/blob/9acae56ce0b0812f3e940cf1f87e73e8d5784e78/src/include/port/win32_port.h#L85
/* Windows doesn't have fsync() as such, use _commit() */
#define fsync(fd) _commit(fd)

// https://github.com/postgres/postgres/blob/874d817baa160ca7e68bee6ccc9fc1848c56e750/src/port/win32fdatasync.c#L23
int
fdatasync(int fd)
{
    // ...
	status = pg_NtFlushBuffersFileEx(handle,
									 FLUSH_FLAGS_FILE_DATA_SYNC_ONLY,
									 NULL,
									 0,
									 &iosb);
    // ...
}
```

#### macOS

It is also worth mentioning macOS. Althrough it is POSIX-complient, but one call to `fsync` is not enough - `fcntl(F_FULLSYNC)` call requred.
This is outlined even in [documentation](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/fsync.2.html).

> For applications that require tighter guarantees about the integrity of their data, Mac OS X provides the *F_FULLFSYNC fcntl*.

Again, this is [handled in etcd](https://github.com/etcd-io/etcd/blob/6f55dfa26e1a359e47e1fb15af79951e97dbac39/client/pkg/fileutil/sync_darwin.go#L36):

```go
// Fsync on HFS/OSX flushes the data on to the physical drive but the drive
// may not write it to the persistent media for quite sometime and it may be
// written in out-of-order sequence. Using F_FULLFSYNC ensures that the
// physical drive's buffer will also get flushed to the media.
func Fsync(f *os.File) error {
	_, err := unix.FcntlInt(f.Fd(), unix.F_FULLFSYNC, 0)
	return err
}

// Fdatasync on darwin platform invokes fcntl(F_FULLFSYNC) for actual persistence
// on physical drive media.
func Fdatasync(f *os.File) error {
	return Fsync(f)
}
```

### Bypass OS page cache

The second solution require us to pass special flag during file opening.

#### Linux

Linux require passing 2 flags `O_SYNC | O_DIRECT` to `open`:

- `O_SYNC` - every write flushed immediately to disk. Also, there is `O_DSYNC` - replace `fsync` with `fdatasync`
- `O_DIRECT` - write bypasses page cache (equivalent to disabling it at all for this file)

Using `O_SYNC`/`O_DSYNC` is equivalent to calling `fsync`/`fdatasync` right after we call `write`/`pwrite`.

Also, using `fcntl` you can change `O_DIRECT` flag, but no `O_SYNC`.
This is specified in documentation for `fcntl`:

> ... It is not possible to change the O_DSYNC and O_SYNC flags; see BUGS, below.

#### Windows

In Windows we have similar flags:

- `FILE_FLAG_NO_BUFFERING` - disables OS page buffering
- `FILE_FLAG_WRITE_THROUGH` - all writes flushed to disk, without buffering

Documentation [gives description](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilea#caching-behavior) of behaviour when both are specified:

> If FILE_FLAG_WRITE_THROUGH and FILE_FLAG_NO_BUFFERING are both specified, so that system caching is not in effect, then the data is immediately flushed to disk without going through the Windows system cache. The operating system also requests a write-through of the hard disk's local hardware cache to persistent media.

And [this blog article](https://devblogs.microsoft.com/oldnewthing/20210729-00/?p=105494) introduces comparison matrix:

<table>
<tr>
  <td colspan="2" rowspan="2"></td>
  <td colspan="2">NO_BUFFERING</td>
</tr>
<tr>
<td>Clear</td>
<td>Set</td>
</tr>
<tr>
  <td rowspan="2">WRITE_THROUGH</td>
  <td>Clear</td>
  <td>Writes go into cache </br>
Lazily written to disk </br>
No hardware flush</td>
  <td>Writes bypass cache </br>
Immediately written to disk </br>
No hardware flush</td>
</tr>
<tr>
  <td>Set</td>
  <td>Writes go into cache</br>
Immediately written to disk</br>
Hardware flush</td>
  <td>Writes bypass cache</br>
Immediately written to disk</br>
Hardware flush</td>
</tr>
</table>

---

Postgres uses this mapping between Windows/Linux flags:

| Windows                 | Unix     |
| ----------------------- | -------- |
| FILE_FLAG_NO_BUFFERING  | O_DIRECT |
| FILE_FLAG_WRITE_THROUGH | O_DSYNC  |

```cpp
// https://github.com/postgres/postgres/blob/30e144287a72529c9cd9fd6b07fe96eb8a1e270e/src/port/open.c#L65
HANDLE
pgwin32_open_handle(const char *fileName, int fileFlags, bool backup_semantics)
{
    // ...
	while ((h = CreateFile(fileName,
                           // ...
						   ((fileFlags & O_DIRECT) ? FILE_FLAG_NO_BUFFERING : 0) |
						   ((fileFlags & O_DSYNC) ? FILE_FLAG_WRITE_THROUGH : 0),
						   NULL)) == INVALID_HANDLE_VALUE)
    // ...
}
```

### Directory sync

#### Linux

But that's not all. Unix way - everythin is file, *even directory*. Directory is a file, but read/write operations are different:

- `read` - iterate directory entries
- `write` - create/delete entry

Read is not very interesting for us, but write is - when we create or delete file (moving file is both operations) performed to must ensure these operations are flushed - `fsync(directory_fd)` is called.
This is [documented](https://man7.org/linux/man-pages/man2/fsync.2.html#DESCRIPTION) as well:

> Calling `fsync()` does not necessarily ensure that the entry in the directory containing the file has also reached disk.
> For that an explicit `fsync()` on a file descriptor for the directory is also needed.

#### Windows

As for Windows - we can not do it:

1. Directory is not openable - we get `EACCESS` when try to do this
2. `FlushFileBuffers` works only with files or whole volume (all files in volume), but for the latter elevated privileges required.

Again, Postgres use *nix style - it call `fsync` for directories, but for Windows it [has additional check](https://github.com/postgres/postgres/blob/874d817baa160ca7e68bee6ccc9fc1848c56e750/src/backend/storage/file/fd.c#L3797):

```cpp
/*
 * fsync_fname_ext -- Try to fsync a file or directory
 *
 * If ignore_perm is true, ignore errors upon trying to open unreadable
 * files. Logs other errors at a caller-specified level.
 *
 * Returns 0 if the operation succeeded, -1 otherwise.
 */
int
fsync_fname_ext(const char *fname, bool isdir, bool ignore_perm, int elevel)
{
	returncode = pg_fsync(fd);

	/*
	 * Some OSes don't allow us to fsync directories at all, so we can ignore
	 * those errors. Anything else needs to be logged.
	 */
	if (returncode != 0 && !(isdir && (errno == EBADF || errno == EINVAL)))
	{
		// ...
		return -1;
	}

	return 0;
}
```

## fsync errors

Even though `write` returned success status code, it does not mean that write was successful - recall that we use buffering, so we just updated in-memory page.

When we call `fsync` it can return error. It was shown in the code above.
But what we should do when we get error code from `fsync`?

Generally - halt immediately. If developing for specific OS - it depends. Why? Main reason, is that we no longer can be sure about consistency:

- File system can be broken
- File can have a hole (invalid write)
- "Dirty" pages can be just marked "clean" and no longer flushed.

Latter may cause differences in file (contents) view from in-memory and disk perspectives, but we will not notice that.

Marking pages as "clean" is a part of Linux implementation - it assumes, that file system correctly completes write operation.

Postgres hackers make a brief overview of different operating system behaviour on `fsync` error:

- Darwin/macOS - invalidate buffers
- OpenBSD - invalidate buffers
- NetBSD - invalidate buffers
- FreeBSD - remain "dirty"
- Linux (after 4.16) - marked "clean"
- Windows - unknown

So, if we are developing crossplatform application - the state of buffers after we get `fsync` error is undefined.
But when developing for specific OS - it is defined.

### fsyncgate

We can not talk about `fsync` errors without touching fsyncgate.

`fsyncgate` - is a name of a bug found in Postgres that caused data loss. More infor can be found [here](https://wiki.postgresql.org/wiki/Fsync_Errors).
Briefly, initially developers thought `fsync` semntics is following:

> If `fsync()` completed successfully, then all writes since last *successful* `fsync()` were flushed to disk.

In another words - try to call `fsync` until it returns a successful status code.
But as we observed earlier it is a misconception. In reality, if `fsync` returned error code, than all "dirty" pages will be just forgotten.
Therefore, more correct to state:

> If `fsync()` completed successfully, then all writes since last ~~successful~~ `fsync()` were flushed to disk.

This was noticed in 2018 year and answer was discussed in [mailing list](https://www.postgresql.org/message-id/CAMsr%2BYFNivjj1eYX0-%3DjfaAi8u%2BQ6CSOXN82_xuALzXAdpWe-Q%40mail.gmail.com).

That bug was fixed in 12 version (with backpatching) in [this commit](https://git.postgresql.org/gitweb/?p=postgresql.git;a=commit;h=9ccdd7f66e3324d2b6d3dec282cfa9ff084083f1) using next code (increase error level):

```cpp
// https://github.com/postgres/postgres/blob/30e144287a72529c9cd9fd6b07fe96eb8a1e270e/src/backend/storage/file/fd.c#L3936
int
data_sync_elevel(int elevel)
{
    return data_sync_retry ? elevel : PANIC;
}

// Любая функция, например эта - https://github.com/postgres/postgres/blob/30e144287a72529c9cd9fd6b07fe96eb8a1e270e/src/backend/access/heap/rewriteheap.c#L1132
void sample_function() 
{
    // Любой вызов fsync в логике 
    if (pg_fsync(fd) != 0)
    // ereport(ERROR,
       ereport(data_sync_elevel(ERROR),
                (errcode_for_file_access(),
                 errmsg("could not fsync file \"%s\": %m", path)));
}
```


{% detail Page cache in databases %}

Disk operations - is one of the most important part of each database. So many of them implement their own page cache manager (subsystem) and does not rely on OS.

This adds some improvements:

- More effective IO operations scheduling (durable write should occur after `COMMIT`)
- More effective (potentially) page eviction algorithms
- Size of page and whole cache can be adjusted

Here some examples of several databases:

| DBMS           | Implementation                                                                                                                                                                                                                                                         | Page eviction algorithm |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------- |
| Postgres       | [bufmgr.h](https://github.com/postgres/postgres/blob/d13ff82319ccaacb04d77b77a010ea7a1717564f/src/include/storage/bufmgr.h), [bufmgr.c](https://github.com/postgres/postgres/blob/d13ff82319ccaacb04d77b77a010ea7a1717564f/src/backend/storage/buffer/bufmgr.c)        | clock-sweep             |
| SQL Server     | sources N/A                                                                                                                                                                                                                                                            | LRU-2 (LRU-K)           |
| Oracle         | sources N/A                                                                                                                                                                                                                                                            | LRU, Temperature-based  |
| MySQL (InnoDB) | [buf0buf.h](https://github.com/mysql/mysql-server/blob/824e2b4064053f7daf17d7f3f84b7a3ed92e5fb4/storage/innobase/include/buf0buf.h), [buf0buf.cc](https://github.com/mysql/mysql-server/blob/824e2b4064053f7daf17d7f3f84b7a3ed92e5fb4/storage/innobase/buf/buf0buf.cc) | LRU                     |

As I said, own page cache/manager can optimize database workflow and smart page eviction algorithm is not only place to optimize:

- Oracle and SQL Server use flash disks as temporal page cache storage instead of flushing them to main disk (settings `DB_FLASH_CACHE_FILE` for Oracle and BPE (buffer pool extension) for SQL Server)
- Postgres have `pg_prewarm` extension to "warm" page cache

{% enddetail %}

> I searched Linux kernel source code and found `fsync` implementation [here](https://github.com/torvalds/linux/blob/67be068d31d423b857ffd8c34dbcc093f8dfff76/fs/buffer.c#L769)

## File system

Usually, we say "write to disk", but actually it is final destination. There is another layer that should be passed - file system.

Nowadays all data stored in filesystems. As for me, I do not know any system that access block devices using either [LBA](https://en.wikipedia.org/wiki/Logical_block_addressing) or [CHS](https://en.wikipedia.org/wiki/Cylinder-head-sector) - you just write at some offset in a file.
So, before data is written to disk it must be processed by file system.

Today there are [huge amount](https://en.wikipedia.org/wiki/Comparison_of_file_systems) of different file systems, so I primarly focus on small part:

- ext family
- btrfs
- xfs
- ntfs

> Choose them, as most popular in my opinion

File systems can be characterized by multiple aspects (i.e. max file name length), but now we are interested in those, who related to fault tolerance.

### File system integrity

From programming perspective, file system - is a global, shared object. Everyone has access to and can to modify it.
Taking global lock during write operations seems a bad idea, so there are a couple of mechanisms different file systems use:

- Journaling (ext family, ntfs, xfs) - file system maintains a log of operations and always able to replay it in case of error
- COW, Copy on write (btrfs, zfs) - blocks of data are not modified, but instead they create new block with modifed data
- Log-structured - file system itself is a log of operations

Not all file system support such mechanisms, i.e. ext2 is not journaling fs and all modifications applied directly.

That seems to be an answer - just choose FS with jounaling/COW and here you go. But no.

Research [Model-Based Failure Analysis of Journaling File Systems](https://research.cs.wisc.edu/wind/Publications/sfa-dsn05.pdf) reveals, that even journaled file systems can end up being inconsistent.
3 file systems (ext3, reiserfs, jfs) were examined when block writing error occurred. Result is presented in table:

![Errors in journaled file systems](https://raw.githubusercontent.com/ashenBlade/habr-posts/file-write/file-write/img/model-based-failure-analysis-figure-2.png)

As you can mention - every file system can leave content of block (Data Block) in inconsistent state (DC, Data Corruption).
Conclusion: do not rely heavily on file system journaling.

I also found [research for NTFS](https://pages.cs.wisc.edu/~laksh/research/Bairavasundaram-ThesisWithFront.pdf) (163 p.) - even with metadata redundancy file system can be corrupted and become unrecoverable.

What about COW and log structured?

In [this research](https://elinux.org/images/b/b6/EMMC-SSD_File_System_Tuning_Methodology_v1.0.pdf) (26 p.) btrfs was tested on SSD and after some power outages file system has become unusable and unrecoverable.
On the other hand, journaled ext4 survived 1406.

As for log-structured - I couldn't find any research about them.

### Magic number

We can outline 2 main components in file, that we should update - file size and data blocks.
What we should update first?

If file size, then in case of failure garbage can be found in file - file size can be larger, but data in new block contains some garbage.

> It can be compared to garbage that stored in initialized variables on stack or in just allocated memory (i.e. using `malloc` in C)

From file system perspective - there is no inconsistency. But there may be in case of application - garbage in data.

We can protect ourselves by using special markers in file - after we read some chunk of data first check that marker.
Usually, it is some predefined constant.

This approach is used by:

- Postgres - add "magic number" in header of WAL page

{% detail Postgres code %}

```cpp
/*
* Each page of XLOG file has a header like this:
*/
#define XLOG_PAGE_MAGIC 0xD114	/* can be used as WAL version indicator */

typedef struct XLogPageHeaderData
{
    uint16		xlp_magic;		/* magic value for correctness checks */
    // ...
} XLogPageHeaderData;
```

{% enddetail %}

- Kafka - ["magic number"](https://github.com/apache/kafka/blob/9bc9fae9425e4dac64ef078cd3a4e7e6e09cc45a/clients/src/main/java/org/apache/kafka/common/record/FileLogInputStream.java#L63) used as a version

{% detail Kafka magic number %}

```java
public class FileLogInputStream implements LogInputStream<FileLogInputStream.FileChannelRecordBatch> {
    @Override
    public FileChannelRecordBatch nextBatch() throws IOException {
        // ...
        
        byte magic = logHeaderBuffer.get(MAGIC_OFFSET);
        final FileChannelRecordBatch batch;

        if (magic < RecordBatch.MAGIC_VALUE_V2)
            batch = new LegacyFileChannelRecordBatch(offset, magic, fileRecords, position, size);
        else
            batch = new DefaultFileChannelRecordBatch(offset, magic, fileRecords, position, size);
        return batch;
    }
}
```

{% enddetail %}

- SQLite - use [ASCII constant string "SQLite format 3"](https://github.com/sqlite/sqlite/blob/f79b0bdcbfb46164cfd665d256f2862bf3f42a7c/src/btree.c#L22) for main database file or [random bytes](https://github.com/sqlite/sqlite/blob/f79b0bdcbfb46164cfd665d256f2862bf3f42a7c/src/pager.c#L754) for journal

{% detail SQLite magic numbers %}

```cpp
// Header for undo journal

/*
** Journal files begin with the following magic string.  The data
** was obtained from /dev/random.  It is used only as a sanity check.
*/
static const unsigned char aJournalMagic[] = {
0xd9, 0xd5, 0x05, 0xf9, 0x20, 0xa1, 0x63, 0xd7,
};

static int readJournalHdr(
Pager *pPager,               /* Pager object */
int isHot,
i64 journalSize,             /* Size of the open journal file in bytes */
u32 *pNRec,                  /* OUT: Value read from the nRec field */
u32 *pDbSize                 /* OUT: Value of original database size field */
){
int rc;                      /* Return code */
unsigned char aMagic[8];     /* A buffer to hold the magic header */
i64 iHdrOff;                 /* Offset of journal header being read */

/* Read in the first 8 bytes of the journal header. If they do not match
** the  magic string found at the start of each journal header, return
** SQLITE_DONE. If an IO error occurs, return an error code. Otherwise,
** proceed.
*/
if( isHot || iHdrOff!=pPager->journalHdr ){
    rc = sqlite3OsRead(pPager->jfd, aMagic, sizeof(aMagic), iHdrOff);
    if( rc ){
    return rc;
    }
    if( memcmp(aMagic, aJournalMagic, sizeof(aMagic))!=0 ){
    return SQLITE_DONE;
    }
}

// ...
}

// Header of main database file

#ifndef SQLITE_FILE_HEADER /* 123456789 123456 */
#  define SQLITE_FILE_HEADER "SQLite format 3"
#endif

/*
** The header string that appears at the beginning of every
** SQLite database.
*/
static const char zMagicHeader[] = SQLITE_FILE_HEADER;

static int lockBtree(BtShared *pBt){
// ...
if( nPage>0 ){
    /* EVIDENCE-OF: R-43737-39999 Every valid SQLite database file begins
    ** with the following 16 bytes (in hex): 53 51 4c 69 74 65 20 66 6f 72 6d
    ** 61 74 20 33 00. */
    if( memcmp(page1, zMagicHeader, 16)!=0 ){
    goto page1_init_failed;
    }
    
// ...

page1_init_failed:
pBt->pPage1 = 0;
return rc;
}
```

{% enddetail %}

Additionally, there are some predefined headers for different file formats. For example, BOM, JPEG or PNG.

### Checksum

But this is just a constant, that does not depend on stored data - it is used to definetely say "this is a garbage".

What go wrong? For example, we send write request and it failed in the middle of operation. Then checksum in file header will be valid, but contents of file are not.
One can think that we should move magic number to the end of such chunk, but who said that data starts to be written from begin to end and *not from end to begin*?

Even so, what happens if data in the middle of page will be corrupted?
Magic number will not detect this, so we need some small digest of our chunk.
We will use it to compare our stored data with those that we have read to ensure integrity.

This small digest called checksum. This is just small fixed-sized byte array computed using some hash functions over all contents of file.
Checksum can be stored both at the begginning and at the end of chunk.
If you have such choice, I recommend store it in the end - data locality: page (OS) with high probability will be already in cache (same for cpu cache, L1/2/3).

Checksum is widely used:

- Postgres - uses [CRC32C for WAL records](https://github.com/postgres/postgres/blob/5f79cb7629a4ce6321f509694ebf475a931608b6/src/include/access/xlogrecord.h#L49):

{% detail Postgres WAL CRC %}

```cpp
typedef struct XLogRecord
{
    // ...
    pg_crc32c	xl_crc;			/* CRC for this record */
} XLogRecord;
```

{% enddetail %}

- etcd - uses [CRC for WAL records](https://github.com/etcd-io/etcd/blob/8b9909e20de0aeacb3e63a0df992347b2f683703/server/storage/wal/walpb/record.pb.go#L28)

{% detail etcd sliding crc %}

```go
type Record struct {
Crc                  uint32   `protobuf:"varint,2,opt,name=crc" json:"crc"`
}
```

{% enddetail %}

- KurrentDB (formerly EventStore) - uses [MD5](https://github.com/EventStore/EventStore/blob/e18b459c0f44c76ff7a1146d023070f5423b759a/src/EventStore.Core/TransactionLog/Chunks/ChunkFooter.cs#L21)

{% detail EventStore MD5 %}

```cpp
public class ChunkFooter {
    // ...
    public readonly byte[] MD5Hash;
    // ...
}
```

{% enddetail %}

### File System consistency model

Before going ahead, we should talk about consistency model.

In programming languages there is such a thing as ["memory model"](https://en.wikipedia.org/wiki/Memory_model_(programming)).
In short, our program can be described in terms of store/load operations (assign value to a variable/read variable's value) and memory model describes which operations can be reordered (more accurately, which reoderings are *prohibited*).
Such reordering can give some performance improvements, but when we are moving to multi-threaded/concurrent execution these reorderings can spoil everything.
Given example:

```cpp
void function() {
    // 1
    a = 123;
    b = 244;

    // 2
    int c = a;
    b = 555; 
}
```

Questions:

- In 1 case - are we allowed to write `b` and only then `a`, so perform store/store reordering
- In 2 case, are we allowed to write `b` and only then read from `a`, so perform store/load reodering.

The memory order answers these questions. For PLs and hardware there is documentation with memory model explanation:

- [C++](https://en.cppreference.com/w/cpp/language/memory_model)
- [C#](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md)
- [Java](https://www.cs.umd.edu/~pugh/java/memoryModel/jsr133.pdf)
- [Python](https://peps.python.org/pep-0583/)
- [Go](https://go.dev/ref/mem)
- [Rust](https://doc.rust-lang.org/nomicon/atomics.html)
- [AMD64](https://www.amd.com/content/dam/amd/en/documents/processor-tech-docs/programmer-references/24592.pdf)

Let's leave programming languages for later. The main thing is that we **can define such rules for the file system as well**.

This is called persistency semantics (name from [this paper](https://people.mpi-sws.org/~viktor/papers/popl2021-persevere.pdf)).

Study ["All File Systems Are Not Created Equal"](https://www.usenix.org/system/files/conference/osdi14/osdi14-paper-pillai.pdf) identified these "basic" operations:

- File chunk overwrite
- Append data to file
- Rename
- Directory operations

After conducting the experiment, this table was compiled:

![Persistency semantics of different file systems](https://raw.githubusercontent.com/ashenBlade/habr-posts/file-write/file-write/img/all-filesystems-not-created-equal-table-1.png)

> Also note, that table shows only *found* bugs - if bug is not found, it does not mean that is does not exist.

For simplicity, I will continue use term "journaled" for file systems that have some kind of buffer, in which all operations stored before being applied - COW, log-structured, journaled and so on.

#### Atomicity

Single logical operation may consists of multiple physical operations. For example, append to a file (write to end) consists of:

1. Update size of file
2. Add new data block

Atomicity in that case means transactional atomicity, like in databases - if we fail to perform even single operation, we must be able rollback state to the initial (with same metadata).

According to that matrix:

1. Any
