# Как писать в файлы?

# Введение

Приветствую. 

Год назад меня сильно увлекла тема отказоустойчивости приложений. Я начал изучать различные аспекты ее реализации в программах и больше всего меня заинтересовал процесс работы с диском. Ресурсов для изучения много, но они все разбросаны по сети и мне понадобилось время, чтобы сложить все кусочки пазла. Здесь я попытаюсь этот пазл собрать воедино, чтобы структуризировать полученные знания.

Для начала разберем путь операции записи, начиная самого приложения.

# Приложение

Все начинается в нашем коде. Обычно имеется интерфейс для работы с файлами. Это зависит от ЯП, но примеры:
- `fwrite` - C
- `std::fstream.write` - C++
- `FileStream.Write` - C#
- `FileOutputStream.Write` - Java
- `open().write` - Python
- `File.Write` - GO

Это все средства предоставляемые языками программирования, для работы с файлами: запись, чтение и т.д.
Их преимуществом является независимость от платформы, на которой мы работаем. 
Но также и привносит свои недостатки. 
В данном случае, это буферизация.

Судя по документации, то из приведенных выше все ЯП используют либо поддерживают буферизацию:
- C - [setvbuf](https://en.cppreference.com/w/c/io/setvbuf)
- C++ - [filebuf](https://cplusplus.com/reference/fstream/filebuf/)
- C# - [BufferedFileStrategy](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/BufferedFileStreamStrategy.cs)
- Java - [Files.newBufferedReader](https://docs.oracle.com/javase/8/docs/api/java/nio/file/Files.html#newBufferedWriter-java.nio.file.Path-java.nio.charset.Charset-java.nio.file.OpenOption...-)
- Python - [io.BufferedIOBase](https://docs.python.org/3/library/io.html#io.BufferedIOBase)
- GO - [bufio.Reader](https://pkg.go.dev/bufio#Reader)

> Насчет C# - в реализации `FileStream` используется `FileStreamStrategy` класс, который обрабатывает запросы. 
> Например, при создании `FileStream` через `File.Open`, `BufferedFileStrategy` обертывает целевой `OSFileStreamStrategy`.

Вообще, буферизация в пространстве пользователя штука неплохая, так как позволяет повысить производительность. 
Но если не знать этого, то часть данных может быть не записана.
Тут может быть 2 случая:
1. Буферизованный файл создан вручную (GO, Java).
2. Буферизация происходит прозрачно для программиста (C, C++).

Если в первом случае мы точно знаем, что буферы надо сборосить после окончания записи, то второй вариант позволит выстрелить себе в ногу:
- Приложение экстренно закроется (например, получили SIGKILL нельзя обработать) и буферы уровня приложения просто не сбросятся.
- Файл после создания будет где-то в памяти и при закрытии буферы сброшены не будут, т.к. просто забудем сделать это.

Выходов здесь 2:
- Сбрасывать буферы после каждого сеанса записи. 
  Например, при логировании мы сначала всю пачку строк записываем через `write` и, только когда все были записаны сбрасываем буфер.
- Убрать буферизацию вообще и делать записи напрямую.

На мой взгляд, более привлекательный вариант - первый. 
Так как он позволит немного повысить производительность.

Для сравнения производительности я провел небольшой бенчмарк.

Записал последовательно в файл 64 Мб данных.
Тестирование производил на 2 машинах:
- Личный ноутбук: NVMe
- Старый сервер: HDD

Результаты следующие:

| Машина | Прямая запись, мс | Буферизированная запись, мс | 
|--------|-------------------|-----------------------------|
| Личный | 75.77             | 62.06                       |
| Старая | 122.1             | 104.9                       |

Как видно, разница заметна: время выполнения при буферизации меньше примерно в 1.2 раза.

<spoiler title="Код бенчмарка">

```cs
TODO: добавить код
```

</spoiler>

# ОС

Язык программирования дает хорошую абстракцию платформы - разработчику не нужно думать (как минимум, не так часто) на какой операционной системе работает приложение.
Но в любом случае функции языка будут транслироваться/превращаться в системные вызовы ОС, для записи в файлы.
Эти системные вызовы специфичны для каждой операционной системы, но в общем случае всегда есть для записи, чтения и открытия файлов.
Например, вот примерное отображение:

| Операция | *nix  | Windows   |
|----------|-------|-----------|
| Открытие | open  | OpenFile  |
| Чтение   | read  | ReadFile  |
| Запись   | write | WriteFile |
| Закрытие | close | CloseFile |

> Под *nix имеют Unix-подобные ОС (Linux, FreeBSD, OSX). 
> Название у этих вызовов одинаковые, хоть и поведение немного отличается.

На уровне ОС тоже присутствует буферизация - [страничный (дисковый) кэш](https://en.wikipedia.org/wiki/Page_cache).
И вот с помощью нее выстрелить в ногу еще проще. 

При работе с файлом (чтение/запись) данные из него читаются страницами, даже когда запрошен только 1 байт (записать или прочитать). 
Когда страница была изменена, то она называется "грязной" и будет сброшена на диск. 
Причем в памяти одновременно может находиться множество страниц, они и создают страничный кэш - буфер уровня ОС.

Так где это может нам повредить? Представим следующий процесс:
1. Нам пришел запрос на обновление данных о пользователе;
2. Мы открываем файл с данными;
3. Переписываем имеющийся диапазон имени переданным значением (представим, что для имени выделено в файле 255 символов);
4. Сообщаем пользователю, что имя успешно обновлено.

Где может возникнуть проблема? После 4 шага. Просто представим, что после отправки ответа пользователю об успешно выполненной операции произошло отключение электричества.
В результате имеем такую ситуацию:
- Пользователь думает, что имя успешно обновлено.
- Данные о пользователе хранятся старые.

Почему старые? Потому что перезаписанное имя хранилось на грязной странице в памяти, а не на диске, и при отключении электричества мы ее сохранить не успели.

Страничный кэш полезен, когда над одним участком памяти производится множество операций чтения/записи. 
Но в случае, когда изменения должны быть "закоммичены" нам нужно удостовериться, что записанные данные действительно были сброшены на диск.
Для этого можно применить несколько стратегий:
1. Системные вызовы для сброса буферов;
2. При открытии файла говорить сразу, что буферизация не нужна.

## Системные вызовы для сброса страниц 

Первый вариант использует специальные системные вызовы.
Для Linux можно использовать:
- `fdatasync(fd)` - проверяет, что данные в памяти и на диске синхронизированы, т.е. выполняет сброс страниц и при необходимости обновляет размер файла;
- `fsync(fd)` - то же самое что и `fdatasync`, но дополнительно синхронизирует метаданные файла (время доступа, изменения и др.);
- `sync_file_range(fd, range)` - проверяет, что указанный диапазон данных файла сброшен на диск;
- `sync()` - эта функция тоже синхронизирует содержимое буферов и дисков, только делает это для всех файлов, а не указанного.

Как уже было сказано, `fdatasync` синхронизирует только содержимое, без метаданных как `fsync`, поэтому он выполняется быстрее.
В etcd делается [явное различие](https://github.com/etcd-io/etcd/blob/6f55dfa26e1a359e47e1fb15af79951e97dbac39/client/pkg/fileutil/sync_linux.go#L32) между этими 2 вызовами:

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

> Больше про эти системные вызовы описано в статье [Устойчивое хранение данных и файловые API Linux](https://habr.com/ru/companies/ruvds/articles/524172/).

Для Windows это:
- `_commit` - сбрасывает данные файла прямо на диск;
- `FlushFileBuffers(fd)` - вызывает запись всех буферных данных в файл (я не Windows разработчик, поэтому не знаю точно чем отличается от предыдущего вызова);
- `NtFlushBuffersFileEx(fd, params)` - сброс страниц на диск, но только для файловых систем NT (NTFS, ReFS, FAT, exFAT).

P.S. `NtFlushBuffersFileEx` [используется в Postgres](https://github.com/postgres/postgres/blob/874d817baa160ca7e68bee6ccc9fc1848c56e750/src/port/win32fdatasync.c#L40) как обертка для кроссплатформенного `fdatasync`.
А для `fsync` - `_commit`

```c++
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


Также стоит упомянуть macOS. Она хоть и является POSIX совместимой, т.е. предоставляет системный вызов `fsync`, но одного его одного недостаточно.
Для нее требуется дополнительный вызов `fcntl(F_FULLSYNC)`. Это также прописывается в [документации](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/fsync.2.html):

> For applications that require tighter guarantees about the integrity of their data, Mac OS X provides the F_FULLFSYNC fcntl.

При разработке на macOS об этом стоит помнить, иначе [вероятность потерять данные высока](https://habr.com/ru/news/653527/).
В etcd это поведение [учитывается](https://github.com/etcd-io/etcd/blob/6f55dfa26e1a359e47e1fb15af79951e97dbac39/client/pkg/fileutil/sync_darwin.go#L36):

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


## Говорим ОС, что синхронизация не нужна

Для второго варианта, нам необходимо открывать файл с необходимым параметром.

В Linux это можно сделать передав параметры `O_SYNC | O_DIRECT` функции `open` при открытии файла.
- `O_SYNC` говорит о том что `write` не должен возвращаться, пока данные не будут точно записаны на диск. Есть еще `O_DSYNC` - это про синхронизацию только данных, без метаданных;
- `O_DIRECT` говорит о том что для записи не нужно использовать страницы, т.е. запись будет происходить в обход страничного кэша.

Использование флага `O_SYNC`/`O_DSYNC` можно сравнить с тем, что после каждого write будет вызываться `fsync`/`fdatasync`, соответственно.

После открытия файла, с помощью `fcntl` можно изменить только `O_DIRECT` флаг, но не `O_SYNC`. Это прописано в описании `fcntl`:
> ... It is not possible to change the O_DSYNC and O_SYNC flags; see BUGS, below.

В Windows для этого есть свои аналоги: флаги `FILE_FLAG_NO_BUFFERING` и `FILE_FLAG_WRITE_THOUGH` для функции открытия файла `CreateFile`:
- `FILE_FLAG_NO_BUFFERING` - отключает буферизацию при записи. Аналог `O_DIRECT`; 
- `FILE_FLAG_WRITE_THOUGH` - каждая запись сразу сбрасывается на диск. Аналог `O_SYNC`;

В [документации](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilea#caching-behavior) дано описание поведению при указании обоих флагов:
> If FILE_FLAG_WRITE_THROUGH and FILE_FLAG_NO_BUFFERING are both specified, so that system caching is not in effect, then the data is immediately flushed to disk without going through the Windows system cache. The operating system also requests a write-through of the hard disk's local hardware cache to persistent media.

## Синхронизация директорий

Но это еще не все. 
Вспомним, что 1) файлы создаются, удаляются и перемещаются и 2) директории - тоже файлы. 
То есть при изменении содержимого директории, ее содержимое должно быть сброшено на диск так же как и файл.

В *nix выполняется это точно так же, как и с файлами - получаем дескриптор директории и выполняем `fsync(directory_fd)` на нее.
Это поведение также задокументированно:

> Calling  fsync() does not necessarily ensure that the entry in the directory containing the file has also reached disk. 
> For that an explicit fsync() on a file descriptor for the directory is also needed.

P.S. Я искал информацию о том, можно ли избежать ручного вызова `fsync` для директории через указание флага `O_SYNC`, но ничего не нашел. 
Если знаете, влияет ли этот флаг на работу с директориями, то подскажите в комментариях. 

Что же касается Windows, то там это сделать нельзя:
1. Директорию нельзя открыть - при попытке происходит ошибка `EACCESS`;
2. `FlushFileBuffers` работает только с файлами, либо томом (все файлы тома сбросить), но для последнего нужны повышенные привилегии.

Это поведение должно учитывать и в Postgres это тоже [проверяется](https://github.com/postgres/postgres/blob/874d817baa160ca7e68bee6ccc9fc1848c56e750/src/backend/storage/file/fd.c#L3797):
```c++
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
	/*
	 * Some OSs require directories to be opened read-only whereas other
	 * systems don't allow us to fsync files opened read-only; so we need both
	 * cases here.  Using O_RDWR will cause us to fail to fsync files that are
	 * not writable by our userid, but we assume that's OK.
	 */
	flags = PG_BINARY;
	if (!isdir)
		flags |= O_RDWR;
	else
		flags |= O_RDONLY;

	/*
	 * Some OSs don't allow us to open directories at all (Windows returns
	 * EACCES), just ignore the error in that case.  If desired also silently
	 * ignoring errors about unreadable files. Log others.
	 */
	if (fd < 0 && isdir && (errno == EISDIR || errno == EACCES))
		return 0;
	else if (fd < 0 && ignore_perm && errno == EACCES)
		return 0;
	else if (fd < 0)
	{
		ereport(elevel,
				(errcode_for_file_access(),
				 errmsg("could not open file \"%s\": %m", fname)));
		return -1;
	}

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

## Ошибки fsync

`fsync` _может_ вернуть ошибку и ее необходимо обработать. 
Это было показано в примере выше.
Если она вернула ошибку, то единственное допустимое действие - завершение работы.
Почему? 
Потому, что после этого мы **не можем быть уверены** в том, что сам **файл остался в согласованном состоянии**, даже если с точки зрения файловой системы он не поломан (об этом будет дальше),
- Файловая система может быть повреждена;
- В файле может появиться дыра (неправильная запись);
- Грязные страницы для записи могут быть помечены чистыми и больше сброшены не будут. 

Последнее может привести к тому, что файл на диске и в памяти имеют разное содержимое, но заметно этого не будет. 

Для справедливости стоит сказать, что пометка страниц чистыми - это часть реализации Linux, так как он предполагает, что файловая система возьмет на себя обязательства корректно закончить операции. 
Вот [тут](https://wiki.postgresql.org/wiki/Fsync_Errors#Research_notes_and_OS_differences) есть примеры поведения различных ОС при ошибке `fsync` (что происходит со страницами):
- Darwin/macOS - отбрасываются;
- OpenBSD - отбрасываются;
- NetBSD - отбрасываются;
- FreeBSD - остаются грязными;
- Linux (после 4.16) - помечаются чистыми;
- Windows - неизвестно.

Можно привести пример, когда некорректное управление этими ошибками приводило к потерям данных - сохранение данных WAL в Postgres.
Изначально, разработчики базы данных предполагали, что семантика `fsync` следующая: 

> Если `fsync()` выполнился успешно, то все записи с момента последнего _успешного_ `fsync` были сброшены на диск.

Т.е. если мы сейчас вызвали `fsync` и он вернул ошибку, то мы можем просто повторить этот вызов в будующем в надежде, что данные в итоге попадут на диск.
Но это ошибочное предположение. 
В реальности, если `fsync` вернул ошибку, то грязные страницы будут просто "забыты", т.е. семантика:

> Если `fsync()` выполнился успешно, то все записи с момента последнего ~~успешного~~ `fsync` были сброшены на диск.

Т.е. все ошибки синхронизации данных с диском просто игнорировались. 
Это было замечено в 2018 году и вопрос поднялся в [списке рассылки](https://lwn.net/Articles/752093/).
Багу даже дали название `fsyncgate 2018` и посвятили отдельную страницу на [вики](https://wiki.postgresql.org/wiki/Fsync_Errors).
Сам баг исправлен в версии 12 (и во многих предыдущих) в [этом коммите](https://git.postgresql.org/gitweb/?p=postgresql.git;a=commit;h=9ccdd7f66e3324d2b6d3dec282cfa9ff084083f1) и если быть точным, то исправили следующим образом:

```c++
// fd.c
int
data_sync_elevel(int elevel)
{
    return data_sync_retry ? elevel : PANIC;
}

// Любая функция
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

<spoiler title="Страничный кэш в БД">

Работа с диском - важная часть любой базы данных. Поэтому многие реализуют свою систему работы со страничным кэшэм и не полагаются на механизмы ОС.
Благодаря этому:
- Более эффективный IO, т.к. запись на диск производится только после `COMMIT`; 
- Используются (потенциально) более оптимальные алгоритмы замещения страниц;
- Размер страницы и их количество в памяти может настраиваться;
- Безопасность работы с данными;

Вот примеры некоторых СУБД:

| СУБД           | Реализация                                                                                                                                                                                                                                                             | Где почитать                                                                                                                                 | Алгоритм замещения страниц |
|----------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------|----------------------------|
| Postgres       | [bufmgr.h](https://github.com/postgres/postgres/blob/d13ff82319ccaacb04d77b77a010ea7a1717564f/src/include/storage/bufmgr.h), [bufmgr.c](https://github.com/postgres/postgres/blob/d13ff82319ccaacb04d77b77a010ea7a1717564f/src/backend/storage/buffer/bufmgr.c)        | [WAL в PostgreSQL: 1. Буферный кеш](https://habr.com/ru/companies/postgrespro/articles/458186/)                                              | clock-sweep                |
| SQL Server     | Исходников не нашел                                                                                                                                                                                                                                                    | [Обзор компонентов управления памятью в SQL Server](https://habr.com/ru/articles/233365/)                                                    | LRU-2 (LRU-K)              |
| Oracle         | Исходников не нашел                                                                                                                                                                                                                                                    | [Oracle Memory Architecture](https://www.appservgrid.com/documentation111/docs/rdbms10g/windows/doc/server.101/b10743/memory.htm#sthref1252) | LRU, Temperature-based     |
| MySQL (InnoDB) | [buf0buf.h](https://github.com/mysql/mysql-server/blob/824e2b4064053f7daf17d7f3f84b7a3ed92e5fb4/storage/innobase/include/buf0buf.h), [buf0buf.cc](https://github.com/mysql/mysql-server/blob/824e2b4064053f7daf17d7f3f84b7a3ed92e5fb4/storage/innobase/buf/buf0buf.cc) | [InnoDB Buffer Pool](https://dev.mysql.com/doc/refman/8.0/en/innodb-buffer-pool.html)                                                        | LRU                        |

Как уже было сказано, собственный менеджер буферов позволяет оптимизировать работу СУБД, поэтому многие разработчики не останавливаются на единственном "умном" алгоритме замещения страниц. Примеры:
- ORACLE и SQL Server имеет возможность использовать flash накопители в качестве временного хранения буферов, вместо сброса на основной диск (параметр `DB_FLASH_CACHE_FILE` для Oracle и расширение Buffer Pool для SQL Server);
- Postgres позволяет "разогревать" кэш страниц с помощью расширения `pg_prewarm`;

</spoiler>

Я немного поресерчил исходники ядра и, возможно, нашел реализацию `fsync` [тут](https://github.com/torvalds/linux/blob/67be068d31d423b857ffd8c34dbcc093f8dfff76/fs/buffer.c#L769)

# Файловая система

Когда речь идет про запись в файлы, обычно говорят "записать на диск". 
Отчасти это верно, но между ОС и диском есть важное связующее звено - файловая система.
Она отвечает за то, как и куда будет производиться запись.

Файловых систем существует [огромное количество](https://en.wikipedia.org/wiki/Comparison_of_file_systems), поэтому я выделю лишь часть:
- ext4, ext3, ext2
- btrfs
- xfs
- ntfs

> Выбрал эти, так как часто упоминаются и многие исследования используют именно их.

Каждая файловая система обладает своими характеристиками, параметрами и особенностями (например, максимальная длина имени файла), но сейчас нас интересуют те, что связаны с записью и сохранностью данных.

## Поддержка целостности файловой системы

Для обеспечения целостности файловой системы могут использоваться различные механизмы:
- Журналирование (ext3, ext4, ntfs, xfs) - файловая система ведет лог операций (WAL)
- Copy-on-write (btrfs, zfs) - при пере/до записи содержимое не меняется, а вместо этого выделяется новый блок, куда новые данные и записываются
- log structured - сама файловая система является большим логом

Но не все файловые системы поддерживают подобные механизмы безопасности. 
Например, ext2 - не журналируемая и любые операции идут сразу в файл.

Вроде бы вот таблетка от проблем - выбирай файловую систему с журналом (или другим механизмом) и радуйся жизни.
Но нет.

Исследование [Model-Based Failure Analysis of Journaling File Systems](https://research.cs.wisc.edu/wind/Publications/sfa-dsn05.pdf) показало, что даже журналируемые файловые системы могут оставить систему в некорректном состоянии.
Было изучено поведение 3 файловых систем (ext3, reiserfs и jfs) при возникновении ошибки записи блока.
Результат их работы приведен в следующей таблице. 

### TODO: таблица

Можно заметить, что каждая из этих файловых систем может оставить содержимое файла (Data Block) в некорректном состоянии (DC, Data Corruption).
То есть, на журналирование полагаться не стоит.

Про NTFS тоже нашел [исследование](https://pages.cs.wisc.edu/~laksh/research/Bairavasundaram-ThesisWithFront.pdf) (страница 163) - даже с учетом дублирования/репликации метаданных файловая система может быть повреждена и не подлежать восстановлению.

А что с другими механизмами - copy-on-write и log structured?

В [этом](https://elinux.org/images/b/b6/EMMC-SSD_File_System_Tuning_Methodology_v1.0.pdf) исследовании (результат на 26 странице) тестировалась btrfs на SSD и после нескольких отключений питания файловая система пришла в негодность и не смогла восстановиться, хотя журналируемый ext4 пережил 1406.  

### TODO: может в отдельную секцию вынести

### TODO: баг файловой системы в конец
Также сами разработчики могут ошибиться в реализации - версии 5.2 и 5.3 btrfs [не рекомендуется](https://bugs.archlinux.org/task/63733) использовать.

Про log structured файловые системы исследований найти не смог.

## Про запись в файл

Обобщая, файл состоит из 2 компонент - метаданные и блоки данных.
При записи в файл надо понять что обновлять первым?

Если метаданные, то после их обновления, в файле может появиться мусор:
1. Делаем запрос на дозапись в файл
2. Файловая система обновляет метаданные - увеличивает длину файла
3. Происходит отказ.

В результате мы имеем файл, который заполнен мусором, хотя ни файловая система, ни диск проблем не видят.

Защититься от этого можно если выставлять специальные маркеры в файле, которые бы нам говорили, что файл инициализирован корректно и все хорошо.

Например, можно использовать константу, чтобы проверять, что дальше действительно идут наши данные, а не мусор.
Такой подход используют:
- Postgres в начале заголовка страницы WAL выставляет magic number 
  ```c++
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
- Kafka [использует magic number](https://github.com/apache/kafka/blob/9bc9fae9425e4dac64ef078cd3a4e7e6e09cc45a/clients/src/main/java/org/apache/kafka/common/record/FileLogInputStream.java#L63) в качестве номера версии
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
- А может быть не числом, а строкой, как в SQLite, причем как [осмысленной](https://github.com/sqlite/sqlite/blob/f79b0bdcbfb46164cfd665d256f2862bf3f42a7c/src/btree.c#L3267), так и [случайной](https://github.com/sqlite/sqlite/blob/f79b0bdcbfb46164cfd665d256f2862bf3f42a7c/src/pager.c#L1608)
  ```c++
  // Заголовок журнала отката - несмысленная константа
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
  
  // Заголовок файла БД - человекочитаемая строка
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

Дополнительно можно выделить заголовки начала файлов разных форматов, например, BOM, JPEG, PNG.

Но это константа, которая не зависит от данных. 
Чаще нужна для пометки границ данных. 
Что будет, если мы успешно запишем ее (константу), а потом в середине записи данных произойдет отказ? 
Часть данных будет записана, а другая нет. 
Причем мы не нельзя сказать, какая именно часть была записана успешно - запись устройство может начать с последней страницы.

Для подобных ситуаций используют чек-суммы, которые рассчитывают по всем записываемым данным. 
Саму чек-сумму можно хранить как в начале, так и в конце.
Я считаю, что лучше записывать чек-сумму в конце - локальность данных:
- Страница для записи, скорее всего, уже будет в памяти, т.е. меньше вероятность промаха страницы (даже если записываем 4 байта, нужно загружать 4 Кб - страницу)
- Меньше потенциальных позиционирований головки диска

Если константа по большей части является простым маркером ("дальше есть данные"), то чек-сумма может помочь в обнаружении нарушения целостности.
Этот подход используют многие приложения.

Для примера:
- Postgres [использует CRC32C](https://github.com/postgres/postgres/blob/5f79cb7629a4ce6321f509694ebf475a931608b6/src/include/access/xlogrecord.h#L49) для проверки целостности записей WAL
  ```c++
  typedef struct XLogRecord
  {
      // ...
      pg_crc32c	xl_crc;			/* CRC for this record */
  } XLogRecord;
  ```
- etcd использует скользящую чек-сумму для [записей WAL](https://github.com/etcd-io/etcd/blob/8b9909e20de0aeacb3e63a0df992347b2f683703/server/storage/wal/walpb/record.pb.go#L28)  
  ```go
  type Record struct {
	Crc                  uint32   `protobuf:"varint,2,opt,name=crc" json:"crc"`
  }
  ```
- EventStore использует [MD5 чек-сумму](https://github.com/EventStore/EventStore/blob/e18b459c0f44c76ff7a1146d023070f5423b759a/src/EventStore.Core/TransactionLog/Chunks/ChunkFooter.cs#L21)
  ```cs
  public class ChunkFooter {
      // ...
      public readonly byte[] MD5Hash;
      // ...
  }
  ```

## Модель согласованности

Прежде чем идти вперед, стоит поговорить о модели согласованности.

Есть такое понятие как модель памяти. 
В разных контекстах я находил разные определения, но скажем, что модель памяти - это правила, которые могут применяться для переупорядочивания store/load операций, т.е. их нарушение запрещается.
Для примера у нас есть следующий участок кода:
```c++
void function() {
    // 1
    a = 123;
    b = 244;

    // 2
    int c = a;
    b = 555; 
}
```

Вопросы такие:
- В 1 случае, можно ли сначала записать `b` и только потом `a`, т.е. изменить порядок store/store операций.
- Во 2 случае, можно ли сначала записать значение `b` и только потом прочитать значение из `a`. 

На такие вопросы отвечает модель памяти.
Для ЯП и железа есть документы с описанием их модели памяти: 
- [C++](https://en.cppreference.com/w/cpp/language/memory_model) 
- [C#](https://github.com/dotnet/runtime/blob/main/docs/design/specs/Memory-model.md)
- [Java](https://www.cs.umd.edu/~pugh/java/memoryModel/jsr133.pdf) 
- [Python](https://peps.python.org/pep-0583/)
- [Go](https://go.dev/ref/mem)
- [Rust](https://doc.rust-lang.org/nomicon/atomics.html
- [AMD64](https://www.amd.com/content/dam/amd/en/documents/processor-tech-docs/programmer-references/24592.pdf)

Но оставим ЯП на потом. 
Сейчас важно понять, что для файловых систем тоже можно определить подобные правила переупорядочивания.

В исследовании [Specifying and Checking File System Crash-Consistency Models](https://www.cs.utexas.edu/~bornholt/papers/ferrite-asplos16.pdf) подобное было названо Crash-Consistency Model - модель согласованности при сбоях. 
Дальше буду использовать это понятие.

В исследовании [All File Systems Are Not Created Equal](https://www.usenix.org/system/files/conference/osdi14/osdi14-paper-pillai.pdf) выделили следующие "базовые" операции:
- Перезапись чанка файла
- Дозапись в файл
- Переименование
- Операции с директориями

После проведения экспериментов с ext2, ext3, ext4, reiserfs, xfs и btrfs была составлена эта таблица:

### TODO: скриншот таблицы
![Свойства файловых систем]()

> Стоит сделать замечание, что это тесты, таблица показывает _найденные воспроизводимые_ нарушения - если бага не найдено, это не значит, что его нет

Для простоты дальше я буду называть журналируемыми файловыми системами те, у которых есть какой-либо условный буфер, в который попадают операции прежде чем примениться - COW, log-structured, soft updates, журнал (всех под одну гербенку).

### Атомарность 

При операциях с файловой системой часто необходимо обновлять сразу несколько мест и во время их обновлений может оказаться в некорректном состоянии.
Например, при дозаписи в файл требуется обновить длину в его inode, создать и инициализировать новый блок данных.

Атомарность, в данном случае, означает атомарность всех этих операций. 

Из таблицы можно сделать следующие выводы:
- Никакая файловая система не может атомарно дозаписать несколько блоков, разве что один
- Перезапись 1 сектора практически везде атомарна
- Нежурналируемые файловые системы почти всегда не предоставляют атомарность операций
- Операции с директориями почти всегда атомарны, за исключением файловых систем вообще без журнала (еще и ext2, но чувствую, что он не так часто используется)

Что может случиться, если произойдет отказ во время операции, которая не является атомарной?
Самое простое - нарушится целостность и придется запускать `fsck` (который не всегда может все восстановить).

В случае, если во время неатомарной операции произойдет сбой:
- В файле окажется мусор, если производилась дозапись - выделили новый блок данных, но ничего не записали, либо записали частично
- Перезапишется только часть данных - операция перезаписи не завершилась до конца

Также и в исследовании [Specifying and Checking File System Crash-Consistency Models](https://www.cs.utexas.edu/~bornholt/papers/ferrite-asplos16.pdf) было изучено поведение нескольких файловых систем в случае отказа.
Была построена следующая таблица:

### TODO: сравнение файловых систем

Закрашенные точки показывают, что какое-то поведение могло привести к нарушению целостности. 
В частности в случае отказа:
- `PA` (Prefix Append, Safe Append) - безопасное добавление не гарантируется всем файловыми системами, т.е. может появиться мусор
- `ARVR` (Atomic Replace Via Rename) - атомарное обновление содержимого существующего файла через его переименование не всеми гарантируется, т.е. при замене старого файла на новый, в файле может оказаться только часть данных
- `ACVR` (Atomic Create Via Rename) - атомарное создание нового файла через переименование никем не гарантируется, т.е. при переименовании временного файла, новый может не содержать всех данных и быть просто нулевой длины

Также стоит обратить внимание на `ARVR` и `ACVR` - это паттерны, которые используются многими приложениями при работе с файлами.
Например, в etcd новые сегменты лога создаются с помощью `ACVR`.

Почему важное? 
Из этой таблицы следует, что операция `rename`, которая используется в этих паттернах, - не атомарна, т.е. старые данные могут попросту исчезнуть, если произойдет отказ во время переименования.
Но если посмотреть в [документацию](https://man7.org/linux/man-pages/man2/rename.2.html#DESCRIPTION) к системному вызову:

> If newpath already exists, it will be atomically replaced, so that there is no point at which another process attempting to access newpath will find it missing.

То есть, в документации прописано, что эта операция атомарна. 
Сначала я не понял кто прав, а кто нет, но потом пришел к следующему выводу:

> `rename` атомарен с точки зрения запущенных в данный момент процессов, но не дает гарантий атомарности в случае отказа. 

Тогда все встает на свои места.

<spoiler title="Важное допущение в исследовании касательно ARVR/ACVR">

В исследовании изучили поведение файловых систем для `ACVR`/`ARVR` и показали, что эти операции могут быть не атомарны. 
Далее, конечно, было объяснение, что так делать нельзя и надо бы вызывать `fsync`.
Чтобы понять насколько это критичное допущение, посмотрим на сам тест (его описание):

```text
# Atomic Replace Via Rename (ARVR)
initial:
  g <- creat("file", 0600)
  write(g, old)
main:
  f <- creat("file.tmp", 0600)
  write(f, new)
  rename("file.tmp", "file")
exists?:
  content("file") 6= old ^ content("file") 6= new
  
# Atomic create via rename (ACVR)
main:
  f <- creat("file.tmp", 0600)
  write(f, data)
  rename("file.tmp", "file")
exists?:
  content("file") 6= ∅ ^ content("file") 6= data
```

Если присмотреться, то между вызовом `write` и `rename` не производится вызов `fsync`.
Что происходит? 
1. Создается файл
2. Вызывается `write` для записи данных
3. Файл переименовывается (для ARVR)
4. Файловая система решает сначала переименовать файл (переупорядочивание операций)
5. Происходит отказ

В результате, после запуска приложения:
- ARVR - содержимое файла пустое, т.к. был переименован пустой на тот момент файл
- ACVR - содержимое файла пустое, т.к. в файл не успели записаться данные 

Если данные для приложения критичны, то оно не будет так себя вести и вызовет `fsync` перед `rename` (об этом будет позже).

Но на всякий случай, разработчики файловых систем делают оптимизации, которые обнаруживают подобные паттерны вызовов и сбрасывают данные на диск перед переименованием:
- ext4 - опция [монтирования `auto_da_alloc`](https://man7.org/linux/man-pages/man5/ext4.5.html) обнаруживает эти паттерны вызовов.
- btrfs - опция [монтирования `flushoncommit`](https://btrfs.readthedocs.io/en/latest/Administration.html) сбрасывает все грязные страницы при вызове `rename` (описание взял [отсюда](https://archive.kernel.org/oldwiki/btrfs.wiki.kernel.org/index.php/FAQ.html#What_are_the_crash_guarantees_of_overwrite-by-rename.3F#What_are_the_crash_guarantees_of_overwrite-by-rename.3F), но вики заархивирован, поэтому, возможно, это уже не так)
- xfs - судя по [рассылке за 2015 год](https://www.spinics.net/lists/xfs/msg36717.html) предложение было рассмотрено, но отклонено (`The sync-after-rename behavior was suggested and rejected for xfs`)

P.S. ссылки на примеры взял [отсюда](https://unix.stackexchange.com/a/464383) 

Подытоживая, `rename` - атомарен, но только если перед ним вызывается `fsync`, чтобы гарантировать, что данные действительно на диске и файл, который собираемся переименовать, - не пустой.

</spoiler>

### Переупорядочивание

Касательно переупорядочивания можно сделать следующий вывод: если файловая система журналируемая, то порядок операций (в большинстве случаев) сохраняется.

Этого нет, например, в ext2, ext3-writeback, ext4-writeback, reiserfs-nolog, reiserfs-writeback и поэтому операции могут спокойно быть переупорядочены.

Также важным является факт переупорядочивания операций над директориями с другими. 
Это означает, что если мы сначала записываем файл, а потом хотим его переименовать, то может случиться так, что мы сначала переименуем файл, (т.е. потенциально старое содержимое потеряем) и только потом начнем запись. 
К моему удивлению, на такое переупорядочивание способны не все, даже некоторые нежурналируемые могут сохранять порядок (xfs не ожидал его здесь увидеть).

### Барьер записи

В модели памяти, есть такое понятие как барьер записи - механизм, который запрещает переупорядочивание таких store/load последовательностей.
Для модели файловой системы он тоже есть - это `fsync()`.

Можно сказать, что этот барьер имеет следующую семантику - все вызовы записи до `fsync()` сброшены на диск. 
Мы не можем сказать, в какой последовательности они были выполнены, но _можем_ утверждать, что они были выполнены, а до этого момента, состояние просто неопределеное.

Вообще, `fsync()` - не барьер записи, просто его можно так использовать. 
Подобная тема уже поднималась - было предложение добавить новый системный вызов `fbarrier()`, который им бы и являлся, но Линус идею [отверг](https://lwn.net/Articles/326505/), посчитав, что это добавит лишнюю сложность.

## Примеры проблем

В том же исследовании All File Systems Are Not Created Equal были анализированы несколько приложений на наличие уязвимостей, касательно моделей согласованности в случае отказа.

В этой таблице описание алгоритмов (порядка вызовов) при работе с диском:
### TODO: таблица с протоколами

А результаты анализа приведены в следующей таблице:

### TODO: таблица с результатами анализа 

Какие из нее можно сделать выводы:
- Приложения, в которых работа с данными критична (СУБД например), часто используют `fsync` в качестве барьера
- Чем хуже предоставляемые файловой системой гарантии касательно атомарности и переупорядочивания - тем больше багов можно обнаружить (и нарушений целостности)
- Каждое приложение имеет свои представления о гарантиях файловых систем и при их нарушении может пострадать целостность. Например, здесь приведены [предположения SQLite](https://sqlite.org/atomiccommit.html#_hardware_assumptions) и сами авторы исследования обнаружили, что ZooKeeper требует атомарности записи в файл лога.

## Другие файловые системы

Исследования выше были ориентированы на файловые системы *nix мира, которые должны быть независимы, но существуют и другие, о который пойдет речь.

### NTFS

NTFS - "стандартная" файловая система для Windows. 
Я не нашел исследований, касательно ее отказоустойчивости, но, опираясь на ее описание, можно сделать следующие выводы:
1. Журналируются только метаданные, т.е. в файлах может появиться мусор
2. Имеет свой транзакционный API, но разработчикам [рекомендуется искать](_https://learn.microsoft.com/en-us/windows/win32/fileio/deprecation-of-txf#abstract) ему альтернативы

### APFS

APFS (Apple File System) - файловая система для Apple, которая должна заменить HFS+.
Из статей выделил следующее:
1. Вместо журналирования используется Copy On Write, причем новая технология `novel copy-on-write metadata scheme` (информацию про нее не нашел)
2. Имеет технологию `Atomic Safe-Save` для гарантии атомарного `rename`
3. Использует чек-суммы только для собственных метаданных, но не данных пользователей

Последний пункт я выделил специально, так как я неправильно понял [статью](https://danluu.com/filesystem-errors#error-detection), на которую опирался.
В ней прописано `apfs doesn’t checksum data because “[apfs] engineers contend that Apple devices basically don’t return bogus data”`, но если перейти на ссылаемую статью, то там прописано `APFS checksums its own metadata but not user data`.
Недопонимание произошло из-за того, что в изначальной статье идет сравнение с ZFS (в которой реализованы чек-суммы данных пользователя), а в той, что изучал я - про нее ни слова.

## Ошибки fsync

В предыдущей главе было сказано, что от ошибок `fsync` не восстановиться и описано как различные ОС обрабатывают подобные ситуации.
Но было сказано только про пометку страниц в памяти.

Данные я взял из исследования [Can Applications Recover from fsync Failures?](https://www.usenix.org/system/files/atc20-rebello.pdf)
Оно говорит само за себя - в нем было проведено исследование поведения различных приложений, файловых систем и ОС в случае ошибки `fsync`.

Для начала посмотрим на таблицу с результатами тестирования файловых систем в случае ошибки `fsync`.

### TODO: таблица

> ext4 data - означает journal режим

Из этой таблицы можно сделать следующие выводы:
- Ошибки `fsync` возникают только в случае проблем с записью блоков данных или журнала, т.к. метаданные сначала журналируются. 
  Но при этом, если ошибка в метаданных будет найдена, то XFS и BTRS станут недоступны для записи (фс либо закроется (XFS), либо перемонтируется в ro режиме соответственно), а ext4 просто залогирует это и продолжит работу.
- При ошибке записи в блоки данных (добавление новых блоков, перезапись), метаданные _могут_ быть обновлены, т.е. размер файла изменится, хотя данные останутся старыми/создастся дыра или мусор.
- Если произойдет ошибка во время записи и файловую систему удастся восстановить, то состояние файла на диске и в ОС может различаться. В примере это btrfs - при ошибке записи, метаданные не меняются, но в памяти сохраняется старый дескриптор файла, который указывает на позицию в файле за его пределами (при записи).
- Все файловые системы помечают страницы чистыми, но это связано с тем, что тесты производились на Linux - в других системах поведение может отличаться (примеры были выше).
- Ошибка `fsync` не обязательно возвращает ошибку, если запись провалилась _сейчас_ - ext4 в data режиме может вернуть ошибку только при следующем вызове, то есть может произойти такая ситуация: 
     1. Записываем данные
     2. `fsync` возвращает успешный статус код
     3. Очищаем буфер с данными для записи
     4. Записываем новую порцию данных
     5. `fsync` возвращает ошибку
     В этом случае, мы уже не можем просто повторить операцию, так как старые данные мы _не сохранили_, а повтор текущей операции может _сохранить данные, которые полагаются на те, что пропали_.
- Не все файловые системы при ошибке перемонтируются в ro режиме - т.е. даже при ошибке `fsync` мы не гарантируем того, что можем приложение на этом узле запустить в read-only режиме.
- ext4 в journal режиме сохраняет целостность только для самой файловой системы, но с точки зрения приложения/пользователя, данные могут быть в некорректном состоянии.

Можно сказать, что работая с файловой системой можно ожидать любого поведения (любая комбинация из возможных поведений как в этой таблице).

Также было исследовано поведение различных приложений в случае ошибки `fsync`.

Результаты приведены в этой таблице

### TODO: таблица

> OV (old value) - возвращение старого значения, а не нового
> FF (false failure) - пользователю говорим, что операция провалилась, но новое значение сохранено
> KC/VC (key/value corruption) - данные были повреждены (тесты на key-value хранилище проводились)
> KNF (key not found) - пользователю говорим, что операция выполнилась, но новое значение не сохранилось на диске (пропало)

Можно сделать следующие выводы:
- Если ошибка `fsync` не проявляется сразу (ext4 journal режим), то ошибок возникает гораздо больше.
- COW файловые системы (на примере btrfs) лучше справляются с ошибками `fsync`, чем обычные журналируемые.
- В случае обнаружения ошибки многие приложения просто останавливаются и откатываются до последнего корректного состояния (`-`, `|` в ячейках таблицы).
- Приложения больше нацеливаются на конкретные ОС, а не файловые системы - поэтому так много различий в поведении для разных файловых систем.

> Интересное замечание авторов - Redis не обращает внимание на код ошибки fsync и всегда возвращает успешный результат. 
> Скорее всего, это потому что Redis в первую очередь In-Memory БД и сохранение данных просто небольшая фича.

# Хранилище

Вот последний этап - постоянное хранилище данных.
Сейчас мы говорим про:
- Жесткие диски, HDD
- Твердотельные накопители, SSD

<spoiler title="Не только HDD/SSD">

Кроме HDD и SSD стоило бы указать еще:
- Леточные накопители
- CD/DVD/Blu-ray диски
- PCM, FRAM, MRAM

Дальше про них разговор вестись не будет, но не хорошо вот так их опустить.

Ленточные носители - хороший вариант для резервного копирования. 
Если верить [этой статье](https://habr.com/ru/companies/x-com/articles/667542/), то они:
1. Самые надежные - 15-30 лет
2. Вмещают много - 18 Тб
3. Мало стоят - 1300 р/Тб (за пример взял [этот картридж](https://ltoshop.ru/lentochnyy_nositeli_kartridzhi/lto_9_ultrium_1845tb/lentochnyy_nositel_dannyh_hpe_lto_9_ultrium_1845tb_worm_q2079w.html)))

CD/DVD/Blu-ray диски продолжают использоваться. 
Судя по [анализу рынка в США](https://www.anythingresearch.com/industry/Manufacturing-Reproducing-Magnetic-Optical-Media.htm) их оборот только увеличивается.
Скорее они подходят для распространения контента (игры, фильмы, музыка), чем для интенсивных IO операций, поэтому тоже пропустим.

Также за бортом я оставил такие технологии как [PCM](https://en.wikipedia.org/wiki/Phase-change_memory) (Phase-change Memory), [FRAM](https://ru.wikipedia.org/wiki/FRAM) (Ferroelectic RAM) и [MRAM](https://en.wikipedia.org/wiki/Magnetoresistive_RAM) (Magnetoresistive RAM).
Данных по ним я не нашел.

</spoiler>

Механизмы хранения, которые они используют, различаются, но сейчас это не главное. 
Главное то, что мы можем выделить параметры, которыми их можно охарактеризовать:
- Износ
- ECC
- Контроллер доступа

## Износ

Каждое оборудование изнашивается.
И если железо (процессор, видеокарта) испортится, то мы это быстро заметим и заменим. 
В общем, ничего страшного не случиться.

Но если умрет хранилище, то мы можем потерять данные.
Blackbaze выпустили [отчет за 2023](https://www.backblaze.com/blog/backblaze-drive-stats-for-2023/) год по статистике отказов своих жестких дисков.
Из него можно сделать следующие выводы:
- [Среднее время жизни HDD](https://www.backblaze.com/blog/wp-content/uploads/2024/02/4-Lifetime-AFR.png) зависит от многих факторов, например, производителя или размера диска, но если вычислить среднее по больнице, то это примерно 65 месяцев (5 лет и 5 месяцев)
- В сравнении с 2022 годом AFR (Annualized Failure Rate, вероятность отказа в году) дисков в среднем увеличился. Это было замечено еще  

Что касается SSD, но у них тоже есть [отчет, но за 2022 год](https://www.backblaze.com/blog/ssd-drive-stats-mid-2022-review/). 
Согласно ему, AFR для SSD - 0.92% (ниже чем у HDD).
Но стоит учитывать, что в эксплуатацию SSD взяли только в 2018 году, поэтому статистику надо еще собрать.

Также стоит поговорить о влиянии физического мира на накопители.
- В HDD больше движущихся деталей, поэтому он сильно подвержен физическим воздействиям. 
  За примерами ходить не надо - [Shouting in the Datacenter](https://www.youtube.com/watch?v=tDacjrSCeq4).
  На этом видео было показано как сильно возрастает задержка ответа HDD, когда на него кричат.
  Но это не единственное - в [этом исследовании](https://www.princeton.edu/~pmittal/publications/acoustic-ashes18.pdf) провели анализ влияния шумов на работу HDD.
  Грубо говоря, HDD заставили работать при постоянных шумах (устроили ему ADoS, Acoustic Denial of Service)  и в результате возросло количество ошибок позиционирования, а в не которых случаях и отказу всего диска.
- Хоть в SSD и нет подобных движущихся деталей, но зато есть электричество. 
  И вот к нему, а точнее его внезапному отключению, он уязвим.
  В [этом исследовании](https://arxiv.org/pdf/1805.00140.pdf) провели тестирование поведения SSD на внезапное отключение электричества во время работы. 
  И, _внезапно_, отключение электричества приводит к нарушению целостности данных, их потере, либо вообще [может превратить SSD](https://www.usenix.org/system/files/conference/fast13/fast13-final80.pdf) в кирпич.

## ECC

HDD и SSD имеют встроенную поддержку ECC - Error Correction Code, Коды Исправления Ошибок:
- HDD имеет поддержку разметки секторов согласно [Advanced Format](https://en.wikipedia.org/wiki/Advanced_Format), который позволяет хранить ECC для всего сектора (для этого нужна дополнительная поддержка со стороны ОС - сегодня она есть практически везде, но в старых системах может отсутствовать)
- SSD тоже имеет подобную поддержку, но только для NAND технологии (в "быту" - обычные SSD, USB флешки, SD карты), но в NOR не так часто (в микроконтроллерах и в ситуациях, когда обновления происходят редко), т.к. последний по своему устройству более надежен. 

Стоит учитывать еще и то, что файловые системы знают [про Advanced Format и могут под него подстраиваться](https://wiki.archlinux.org/title/Advanced_Format#File_systems), но это за рамками статьи.

Но даже если ECC и есть, то он не всегда может справиться с ошибками. 
В [этом исследовании](https://arxiv.org/pdf/2012.12373.pdf) сравнивали жизненные циклы HDD и SSD путем тестирования их на собственных датасетах.
Эти графики показывают сколько было обнаружено неисправимых ошибок (UE, Uncorrectable Error) до момента отказа диска, т.е. ошибок, которые не могли исправить ECC

### TODO: таблица 12 с UE 

Выводы можно сделать следующие:
- Количество неисправленных ошибок у SSD зависит от времени жизни диска, тогда HDD - от времени позиционирования головки диска (Head Flying Hours)
- Перед отказом количество ошибок HDD резко возрастает за 2 дня
- Количество неисправленных на SSD больше, чем для HDD

Главный вывод - ECC иногда может не справляться с нарушением целостности.

В итоге, можно сказать, что чаще всего (современное оборудование и версии ОС) накопители имеют коды коррекции ошибок, но полностью полагаться на них не стоит.

## Контроллер доступа

Последний элемент, который стоит рассмотреть - это контроллер накопителя.
Этот компонент знает как работать с физическим хранилищем и обрабатывает приходящие от ОС запросы.
Важной деталью здесь является дисковый кэш - помимо страничного буфера есть еще и дисковый кэш.
Туда попадают все модифицирующие операции.

Вспомним про `fsync` - он должен обеспечивать сброс всех данных на диск, т.е. возвращается только когда данные уже на диске.
Если посмотреть man для `fsync` сейчас, то можно увидеть:

> The fsync() implementations in older kernels and lesser used filesystems do not know how to flush disk caches. In these cases disk caches need to be disabled using hdparm(8) or sdparm(8) to guarantee safe operation. 

Т.е. в старых версиях ядра (судя по указанной ранее версии 2.2) `fsync` не знал как правильно сбрасывать кэш диска, как не знали и некоторые малоиспользуемые файловые системы.

Если верить статье [Ensuring data reaches disk](https://lwn.net/Articles/457667/), то начиная с версии 2.6.35 ext3, ext4, xfs и btrfs могут быть смонтированы с флагом `barrier`, чтобы включить барьеры (сброс дискового кэша).
По крайней мере, в man странице для [mount](https://man.linuxexplore.com/htmlman8/mount.8.html) эти файловые системы имеют флаг `barrier`.

Я немного поискал в [исходниках Linux](https://github.com/torvalds/linux/tree/a4145ce1e7bc247fd6f2846e8699473448717b37/fs) файловые системы, которые не умеют выполнять fsync, но пришел к выводу, что `fsync` не реализован только в readonly файловых системах (например, [efs](https://github.com/torvalds/linux/blob/67be068d31d423b857ffd8c34dbcc093f8dfff76/fs/efs/dir.c#L13) и [isofs](https://github.com/torvalds/linux/blob/67be068d31d423b857ffd8c34dbcc093f8dfff76/fs/isofs/dir.c#L268) не регистрируют `fsync`).
Также если файловая система не имеет своей логики сброса данных (например, не журналируемая), то всегда может использовать обобщенную реализацию `fsync` - с помощью комбинаций других системных вызовов:
```c++
// https://github.com/torvalds/linux/blob/a4145ce1e7bc247fd6f2846e8699473448717b37/block/bdev.c#L203
/*
 * Write out and wait upon all the dirty data associated with a block
 * device via its mapping.  Does not take the superblock lock.
 */
int sync_blockdev(struct block_device *bdev)
{
	if (!bdev)
		return 0;
	return filemap_write_and_wait(bdev->bd_inode->i_mapping);
}
EXPORT_SYMBOL(sync_blockdev);

// https://github.com/torvalds/linux/blob/a4145ce1e7bc247fd6f2846e8699473448717b37/mm/filemap.c#L779
/**
 * file_write_and_wait_range - write out & wait on a file range
 * @file:	file pointing to address_space with pages
 * @lstart:	offset in bytes where the range starts
 * @lend:	offset in bytes where the range ends (inclusive)
 *
 * Write out and wait upon file offsets lstart->lend, inclusive.
 *
 * Note that @lend is inclusive (describes the last byte to be written) so
 * that this function can be used to write to the very end-of-file (end = -1).
 *
 * After writing out and waiting on the data, we check and advance the
 * f_wb_err cursor to the latest value, and return any errors detected there.
 *
 * Return: %0 on success, negative error code otherwise.
 */
int file_write_and_wait_range(struct file *file, loff_t lstart, loff_t lend)
{
	int err = 0, err2;
	struct address_space *mapping = file->f_mapping;

	if (lend < lstart)
		return 0;

	if (mapping_needs_writeback(mapping)) {
		err = __filemap_fdatawrite_range(mapping, lstart, lend,
						 WB_SYNC_ALL);
		/* See comment of filemap_write_and_wait() */
		if (err != -EIO)
			__filemap_fdatawait_range(mapping, lstart, lend);
	}
	err2 = file_check_and_advance_wb_err(file);
	if (!err)
		err = err2;
	return err;
}
EXPORT_SYMBOL(file_write_and_wait_range);

// https://github.com/torvalds/linux/blob/a4145ce1e7bc247fd6f2846e8699473448717b37/fs/hfs/inode.c#L661
static int hfs_file_fsync(struct file *filp, loff_t start, loff_t end,
			  int datasync)
{
    // ...
	file_write_and_wait_range(filp, start, end);
	// ...	
	sync_blockdev(sb->s_bdev);
	// ...	
	return ret;
}
```

## Гарантии записи

В конце хочется поговорить о том, какие гарантии записи дают разные устройства. 
Под этим я сейчас подразумеваю 2 вещи:
- Атомарность записи
- PowerSafe OverWrite (PSOW)

### Атомарность записи

Чтение и запись на диск производится не по 1 байту за раз, а блоками, по несколько.

Для HDD единица чтения и записи - сектор.
Но для SSD единицы чтения и записи разные. 
Единица чтения - страница, а записи - блок.

Но сейчас больше интересна атомарность записи, поэтому внимание обращать будем на единицу записи.
Согласно этому ответу [на StackOverflow](https://stackoverflow.com/a/61832882) - запись скорее всего (likely) атомарна, но при условии, что:
- Контроллер диска имеет запасную батарею
- Вендор SCSI диска дает гарантии атомарности записи 
- Для NVMe вызывается функция для атомарной записи

Звучит вполне логично, поэтому примем это за ответ 

### PowerSafe OverWrite (PSOW)

[PowerSafe OverWrite](https://www.sqlite.org/psow.html) - это термин, который используют разработчики SQLite для описания поведения некоторых файловых систем и дисков в случае внезапного отключения электричества.
Заключается оно в следующем:

> When an application writes a range of bytes in a file, no bytes outside of that range will change, even if the write occurs just before a crash or power failure.

Перевод:

> В случае отказа или отключения питания во время записи диапазона байтов в файл, никакие данные за пределами этого диапазона не будут изменены.

В практическом смысле, это свойство означает наличие батареи в накопителе на случай отключения электричества для безопасной записи последних данных секторы.

<spoiler title="Атомарность и PSOW - не пересекаются">

Атомарность и PowerSafe OverWrite - это разные характеристики и одно не является частным случаем другого.

Для примера рассмотрим такую ситуацию - мы хотим перезаписать участок файла и в момент перезаписи отключилось электричество.
В зависимости от различных комбинаций, последствия будут разными.

Представим, что у нас есть 3 сектора, заполненных 0, и хотим перезаписать определенный диапазон единицами, причем этот диапазон затрагиваетс.
Изобразим следующим образом.

```text
              А         Б         В
Секторы: |000000000|000000000|000000000|        
Запись:       |------------------|
```

Тогда в зависимости от свойств последствия могут быть следующими:

1. Atomic + PSOW

   Каждый сектор содержит либо старые данные, либо полностью обновленные данные (биты были перезаписаны).

   Возможные ситуации:
   ```text
   А: |000011111|000000000|000000000|
           |------------------|
   
   Б: |000000000|111111111|000000000|
           |------------------|
   
   В: |000000000|000000000|111100000|
           |------------------|
   ```

2. !Atomic + PSOW

   Представим, что при записи в сектор А произошел сбой. 
   Тогда в диапазоне от начала записи и до конца возможно любое состояния, т.е. будут смешаны старые и новые данные, но биты за пределами этого сектора затронуты не будут.

   Тогда возможны такие ситуации:
   ```text
   А: |000011010|000000000|000000000|
           |------------------|
   
   Б: |000000000|110011010|000000000|
           |------------------|
   
   В: |000000000|000000000|001000000|
           |------------------|
   ```
   
   Главное заметим, что данные за пределами этого диапазона не были изменены.

3. Atomic + !PSOW
  
   Дела становятся интереснее, когда запись в сектор атомарна, но PSOW гарантировать не можем.
   Пример подобного поведения привели разработчики SQLite: при перезаписи участка файла ОС считывает весь сектор, изменяет в нем нужные байты, записывает на диск (Read Modify Write) и в момент записи происходит отключение электричества.
   Данные были записаны только частично, ECC не был обновлен и при запуске диск обнаруживает, что сектор некорректный и зануляет его.
   Хоть запись и атомарна, но данные за пределами диапазона были изменены.

   В нашем примере это можно представить следующим образом (единицы означают чистые страницы):
   ```text
   А: |111111111|000000000|000000000|
           |------------------|
   
   Б: |000000000|111111111|000000000|
           |------------------|
   
   В: |000000000|000000000|111111111|
           |------------------|
   ```

4. !Atomic + !PSOW
   
   Это последний и самый страшный пример. 
   В этом случае, мы никаких гарантий не даем и абсолютно любое изменение единственного байта может привести к инвалидации всего сектора.
   
   Картина в данном случае похожа на 3 случай.

В репозитории [hashicorp/raft-wal](https://github.com/hashicorp/raft-wal) имеется README, в котором описаны некоторые приложения и их предположения относительно гарантий. 

| Приложение                                                                                            | Атомарность | PowerSafe OverWrite |
|-------------------------------------------------------------------------------------------------------|-------------|---------------------|
| [SQLite](https://sqlite.org/atomiccommit.html#_hardware_assumptions)                                  | -           | + (начиная с 3.7.9) |
| [Hashicorp](https://github.com/hashicorp/raft-wal/tree/main?tab=readme-ov-file#our-assumptions)       | -           | +                   |
| [Etcd/wal](https://github.com/hashicorp/raft-wal/tree/main?tab=readme-ov-file#user-content-etcd-wal)  | +           | +                   |
| [LMDB](https://github.com/hashicorp/raft-wal/tree/main?tab=readme-ov-file#user-content-lmdb)          | +           | -                   |
| [BoltDB](https://github.com/hashicorp/raft-wal/tree/main?tab=readme-ov-file#user-content-rocksdb-wal) | +           | +                   |

</spoiler>

На этом можно было бы и закончить, но мы пропустили один довольно важный слой - среда выполнения.

# Рантайм

В самом начале мы перешли от приложения сразу к операционной системе. 
Но между ними может лежать еще один слой - рантайм:
- Среда выполнения - Node.js, .NET, JVM
- Интерпретаторы - Python, Ruby

Если при работе в C/C++ можно сразу вызвать `fsync`, то для других яп надо учитывать различные аспекты рантайма.

Сейчас поговорим про `fsync`, так как он необходим для подтверждения сохранности данных.

В java имеется метод `force(true)` для этого. В документации написано, что этот метод

> Forces any updates to this channel's file to be written to the storage device that contains it.

То есть напрямую `fsync` не вызывается, мы полагаемся на интерфейс, который среда предлагает.

То же самое можем увидеть в .NET - у класса `FileStream` есть перегруженный метод `Flush(bool flushToDisk)`.
Если ему передать значение `true`, то все данные будут записаны на диск:

> Use this overload when you want to ensure that all buffered data in intermediate file buffers is written to disk. When you call the Flush method, the operating system I/O buffer is also flushed.

Но стоит заметить, что ничего про `fsync` не сказано.
Да, это платформозависимая деталь реализации, но если мы хотим точно убедиться в сохранности лучше проверить.

Я решил проверить, как себя ведет вызов этого метода. 
Сначала поискал в исходниках и нашел следующую цепочку вызовов:

<spoiler title="Цепочка вызовов">

```cs
public class FileStream
{
    private readonly FileStreamStrategy _strategy;
    
    // https://github.com/dotnet/runtime/blob/da781b3aab1bc30793812bced4a6b64d2df31a9f/src/libraries/System.Private.CoreLib/src/System/IO/FileStream.cs#L389
    public virtual void Flush(bool flushToDisk)
    {
        if (_strategy.IsClosed)
        {
            ThrowHelper.ThrowObjectDisposedException_FileClosed();
        }

        _strategy.Flush(flushToDisk);
    }
}

internal abstract class OSFileStreamStrategy : FileStreamStrategy
{
    // https://github.com/dotnet/runtime/blob/da781b3aab1bc30793812bced4a6b64d2df31a9f/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/OSFileStreamStrategy.cs#L137
    internal sealed override void Flush(bool flushToDisk)
    {
        if (flushToDisk && CanWrite)
        {
            FileStreamHelpers.FlushToDisk(_fileHandle);
        }
    }
}

internal static partial class FileStreamHelpers
{
    // https://github.com/dotnet/runtime/blob/da781b3aab1bc30793812bced4a6b64d2df31a9f/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/FileStreamHelpers.Unix.cs#L40
    internal static void FlushToDisk(SafeFileHandle handle)
    {
        if (Interop.Sys.FSync(handle) < 0)
        {
            Interop.ErrorInfo errorInfo = Interop.Sys.GetLastErrorInfo();
            switch (errorInfo.Error)
            {
                case Interop.Error.EROFS:
                case Interop.Error.EINVAL:
                case Interop.Error.ENOTSUP:
                    // Ignore failures for special files that don't support synchronization.
                    // In such cases there's nothing to flush.
                    break;
                default:
                    throw Interop.GetExceptionForIoErrno(errorInfo, handle.Path);
            }
        }
    }
}

internal static partial class Interop
{
    internal static partial class Sys
    {
        // https://github.com/dotnet/runtime/blob/da781b3aab1bc30793812bced4a6b64d2df31a9f/src/libraries/Common/src/Interop/Unix/System.Native/Interop.FSync.cs#L11
        [LibraryImport(Libraries.SystemNative, EntryPoint = "SystemNative_FSync", SetLastError = true)]
        internal static partial int FSync(SafeFileHandle fd);
    }
}

// https://github.com/dotnet/runtime/blob/da781b3aab1bc30793812bced4a6b64d2df31a9f/src/native/libs/System.Native/pal_io.c#L736
int32_t SystemNative_FSync(intptr_t fd)
{
    int fileDescriptor = ToFileDescriptor(fd);

    int32_t result;
    while ((result =
#if defined(TARGET_OSX) && HAVE_F_FULLFSYNC
    fcntl(fileDescriptor, F_FULLFSYNC)
#else
    fsync(fileDescriptor)
#endif
    < 0) && errno == EINTR);
    return result;
}
```

</spoiler>

То есть, при указании `true`, должен произойти вызов `fsync`. Дальше я захотел проверить это в реальности. Для этого написал следующий код и отследил его выполнение с помощью `strace`.

```cs
using var file = new FileStream("sample.txt", FileMode.OpenOrCreate);
file.Write("hello, world"u8);
file.Flush(true);
```

Вот часть вывода `strace` с открытия и до закрытия файла.

```shell
openat(AT_FDCWD, "/path/sample.txt", O_RDWR|O_CREAT|O_CLOEXEC, 0666) = 19
lseek(19, 0, SEEK_CUR)                  = 0
pwrite64(19, "hello, world", 12, 0)     = 12
fsync(19)                               = 0
flock(19, LOCK_UN)                      = 0
close(19)                               = 0
```

По шагам:
1. `openat` - Открыт файл с дескриптором 19
2. `lseek` - Указатель смещен в самое начала
3. `pwrite64` - Записаны наши данные
4. `fsync(19)` - Вызов `fsync`, сброс данных на диск
5. `close(19)` - Закрыли файл

Вот и хорошо - `fsync` вызывается. Но для запуска я использовал версию .NET 8.0.1. Мне стало интересно, а что будет на других версиях. Я выставил версию .NET 7 (7.0.11), скомпилировал с теми же параметрами и запустил `strace` уже для него:

```shell
openat(AT_FDCWD, "/path/sample.txt", O_RDWR|O_CREAT|O_CLOEXEC, 0666) = 19
lseek(19, 0, SEEK_CUR)                  = 0
pwrite64(19, "hello, world", 12, 0)     = 12
flock(19, LOCK_UN)                      = 0
close(19)                               = 0
```

В последних строках нет `fsync`! Более того, если вызвать `Flush(true)` еще раз, то он появится:
```shell
openat(AT_FDCWD, "/home/ash-blade/Projects/habr-posts/file-write/src/FileWrite.FsyncCheckNet7/bin/Release/net7.0/sample.txt", O_RDWR|O_CREAT|O_CLOEXEC, 0666) = 19
lseek(19, 0, SEEK_CUR)                  = 0
pwrite64(19, "hello, world", 12, 0)     = 12
fsync(19)                               = 0
flock(19, LOCK_UN)                      = 0
close(19)                               = 0
```

В итоге, я пришел к выводу, что первый `Flush(true)` по каким-то причинам игнорируется, а последующие успешно вызывают `fsync`.

Также стоит поговорить о возможностях, предоставляемых самим языком.
Например, `fsync` может (и должен) вызывать на директориях, чтобы убедиться в создании или удалении файлов, поэтому нам необходимо получить файловый дескриптор директории.

Тут я опять пожалуюсь на C# - понятия дескриптор для директории тут нет (наследие Windows). 
А получить дескриптор директории обычными способами нельзя - у класса `Directory` нет метода `Open` или какого-нибудь наподобие `Sync`, а если передать в `FileStream` директорию, даже с указанием ReadOnly режима, то возникнет исключение `UnauthorizedAccessException`.

Я нашел обходной путь: с помощью импорта функции `open` получаем дескриптор директории, а после создаем `SafeFileHandle`, в который его и передаем. 
В этом случае, исключений нет и `fsync` вызывается.

```cs
var directory = Directory.CreateDirectory("sample-directory");
const int directoryFlags = 65536; // O_DIRECTORY | O_RDONLY
var handle = Open(directory.FullName, directoryFlags); 
using var stream = new FileStream(new SafeFileHandle(handle, true), FileAccess.ReadWrite);
stream.Flush(true);

[DllImport("libc", EntryPoint = "open")]
static extern nint Open(string path, int flags);
```

И вот `strace`

```shell
openat(AT_FDCWD, "/path/sample-directory", O_RDONLY|O_DIRECTORY) = 19
lseek(19, 0, SEEK_CUR)                  = 0
lseek(19, 0, SEEK_CUR)                  = 0
fsync(19)                               = 0
close(19)                               = 0
```

# Паттерны файловых операций

После рассмотрения возможных сбоев и ошибок, рассмотрим как с ними можно бороться.

## Создание нового файла

Жизнь файла начинается с его создания.
Вспомним, что просто создать файл нельзя - его на диске может и не быть к моменту возвращения функции создания.
Поэтому, если нам нужно создать файл, то после `creat` вызываем `fsync`:
1. `creat("/dir/data")` - Создаем файл с данными
2. `fsync("/dir")` - Актуализируем содержимое директории файла

Изначально, файл пуст. Но что если файл уже должен быть проинициализирован? Как мы уже видели последовательность "создать файл", "записать данные", "закрыть" не работает, так как при отказе в файле может быть мусор, окажется пустым или, вообще, не существовать.

Для создания полностью инициализированных файлов используют паттерн `Atomic Create Via Rename`.
Имя говорит само за себя - для создания нового файла мы используем операцию переименования.

Алгоритм для этого следующий:
1. `creat("/dir/data.tmp")` - Создаем временный файл
2. `write("/dir/data.tmp", new_data)` - Записываем в этот файл необходимые данные
3. `fsync("/dir/data.tmp")` - Сбрасываем данные временного файла
4. `rename("/dir/data.tmp", "/dir/data")` - Переименовываем временный файл в целевой
5. `fsync("/dir")` - Актуализируем содержимое директории файла

В качестве примера - создание нового файла лога в [etcd](https://github.com/etcd-io/etcd/blob/4527093b166da6fb1d803649d7c0d7aef7d9b878/server/storage/wal/wal.go#L716):

```go
// cut closes current file written and creates a new one ready to append.
// cut first creates a temp wal file and writes necessary headers into it.
// Then cut atomically rename temp wal file to a wal file.
func (w *WAL) cut() error {
	// Название для нового файла сегмента
	fpath := filepath.Join(w.dir, walName(w.seq()+1, w.enti+1))

    // 1. Создание временного файла
	newTail, err := w.fp.Open()
	if err != nil {
		return err
	}

    // 2. Записываем данные во временный файл
	// update writer and save the previous crc
	w.locks = append(w.locks, newTail)
	prevCrc := w.encoder.crc.Sum32()
	w.encoder, err = newFileEncoder(w.tail().File, prevCrc)
	if err != nil {
		return err
	}
	if err = w.saveCrc(prevCrc); err != nil {
		return err
	}
	if err = w.encoder.encode(&walpb.Record{Type: MetadataType, Data: w.metadata}); err != nil {
		return err
	}
	if err = w.saveState(&w.state); err != nil {
		return err
	}

	// atomically move temp wal file to wal file
    
    // 3. Сбрасываем данные файла на диск
	if err = w.sync(); err != nil {
		return err
	}

    // 4. Переименовываем временный файл в целевой
	if err = os.Rename(newTail.Name(), fpath); err != nil {
		return err
	}
	
	// 5. Сбрасываем содержимое директории на диск
	if err = fileutil.Fsync(w.dirFile); err != nil {
		return err
	}

	// reopen newTail with its new path so calls to Name() match the wal filename format
	newTail.Close()
	if newTail, err = fileutil.LockFile(fpath, os.O_WRONLY, fileutil.PrivateFileMode); err != nil {
		return err
	}

	w.locks[len(w.locks)-1] = newTail
	prevCrc = w.encoder.crc.Sum32()
	w.encoder, err = newFileEncoder(w.tail().File, prevCrc)
	if err != nil {
		return err
	}

	return nil
}
```

Дополнительными комментариями я отметил соответствие описанных шагов в алгоритме и того, что выполняется в коде. Шаги - одни и те же и в той же последовательности.
Также, в конце файл заново открывается, но это необходимо для того, чтобы получать актуальное название файла, а не временное (с которым создали), и на целостность не влияет.

## Изменение файла

Файл у нас есть. Теперь в него необходимо внести изменения. Тут 2 варианта.

### Изменение небольшого файла

В случае, если файл небольшого размера, то мы можем применить паттерн `Atomic Replace Via Rename`.
Алгоритм тот же, что и для `Atomic Create Via Rename`:
1. `creat("/dir/data.tmp")` - Создаем временный файл
2. `write("/dir/data.tmp", new_data)` - Записываем в этот файл необходимые данные
3. `fsync("/dir/data.tmp")` - Сбрасываем необходимые данные в файл
4. `rename("/dir/data.tmp", "/dir/data")` - Переименовываем временный файл в целевой
5. `fsync("/dir")` - Сбрасываем содержимое данных файла

Применение этого паттерна можно увидеть в LevelDB - сброс данных на диск из памяти, Compaction (вообще, переводится как "уплотнение", но логика именно сохранения данных на диск):

```c++
// https://github.com/google/leveldb/blob/068d5ee1a3ac40dabd00d211d5013af44be55bea/db/db_impl.cc#L549
void DBImpl::CompactMemTable() {
  // ...

  // Replace immutable memtable with the generated Table
  if (s.ok()) {
    edit.SetPrevLogNumber(0);
    edit.SetLogNumber(logfile_number_);  // Earlier logs no longer needed
    s = versions_->LogAndApply(&edit, &mutex_); // Вызывается метод LogAndApply у класса VersionSet
  }
  
  // ...
}

// https://github.com/google/leveldb/blob/068d5ee1a3ac40dabd00d211d5013af44be55bea/db/version_set.cc#L777
Status VersionSet::LogAndApply(VersionEdit* edit, port::Mutex* mu) {
  // 1. Создаем новое состояние - применяем правки к текущему состоянию (пока в памяти)
  Version* v = new Version(this);
  {
    Builder builder(this, current_);
    builder.Apply(edit);
    builder.SaveTo(v);
  }
  Finalize(v);

  // Initialize new descriptor log file if necessary by creating
  // a temporary file that contains a snapshot of the current version.
  std::string new_manifest_file;
  Status s;
  if (descriptor_log_ == nullptr) { 
    // 2. Создаем временный файл
    new_manifest_file = DescriptorFileName(dbname_, manifest_file_number_);
    s = env_->NewWritableFile(new_manifest_file, &descriptor_file_);
    if (s.ok()) {
      // 3. Записываем во временный файл новый снапшот БД
      descriptor_log_ = new log::Writer(descriptor_file_);
      s = WriteSnapshot(descriptor_log_);
    }
  }

  {
    if (s.ok()) {
      // 4. Сбрасываем данные на диск
      s = descriptor_file_->Sync();
    }

    // If we just created a new descriptor file, install it by writing a
    // new CURRENT file that points to it.
    if (s.ok() && !new_manifest_file.empty()) {
      // 5. Переименовываем временный файл
      s = SetCurrentFile(env_, dbname_, manifest_file_number_);
    }

    mu->Lock();
  }
  return s;
}

// https://github.com/google/leveldb/blob/068d5ee1a3ac40dabd00d211d5013af44be55bea/util/env_posix.cc#L334
class PosixWritableFile: public WritableFile {
public:
  Status Sync() override {
    // На всякий случай вызываем fsync для содержащей файл директории (для снапшота - наш случай)
    Status status = SyncDirIfManifest();
    
    status = FlushBuffer();
    return SyncFd(fd_, filename_);
  }
private:
  static Status SyncFd(int fd, const std::string& fd_path) {
#if HAVE_FULLFSYNC
    // On macOS and iOS, fsync() doesn't guarantee durability past power
    // failures. fcntl(F_FULLFSYNC) is required for that purpose. Some
    // filesystems don't support fcntl(F_FULLFSYNC), and require a fallback to
    // fsync().
    if (::fcntl(fd, F_FULLFSYNC) == 0) {
      return Status::OK();
    }
#endif  // HAVE_FULLFSYNC

#if HAVE_FDATASYNC
    bool sync_success = ::fdatasync(fd) == 0;
#else
    bool sync_success = ::fsync(fd) == 0;
#endif  // HAVE_FDATASYNC

    if (sync_success) {
      return Status::OK();
    }
    return PosixError(fd_path, errno);
  }
}

// https://github.com/google/leveldb/blob/068d5ee1a3ac40dabd00d211d5013af44be55bea/db/filename.cc#L123
Status SetCurrentFile(Env* env, const std::string& dbname,
                      uint64_t descriptor_number) {
  // ...
  if (s.ok()) {
    // Переименовываем временный файл в настоящий/целевой
    s = env->RenameFile(tmp, CurrentFileName(dbname));
  }
  return s;
}
```

### Изменение большого файла

Но что делать, если файл большой или памяти на диске мало?

Вспомним с чего начинали - простая перезапись файла. Но теперь доработаем эту операцию так, чтобы она стала отказоустойчивой. Данный пример я взял из статьи [Files Are Hard](https://danluu.com/file-consistency/). 
При записи в файл может случиться отказ и тогда, возможно, весь файл повредится. Вспомним, что может случиться:
- Операции переупорядочиваются
- Записаться может только часть данных
- В файле может появиться мусор
- Целостность содержимого может быть нарушена (даже после успешной записи)

Для предоставления отказоустойчивости используется лог операций:
- undo (rollback) - откат изменений
- redo (write ahead log, wal) - завершение операций

Немного подробнее про применение этих логов в базах данных можно почитать [тут](http://www.cburch.com/cs/340/reading/log/index.html). Теперь - как делать отказоустойчивое изменение данных файла

#### Undo лог

Undo лог хранит в себе данные, которые необходимы для отката операций. В случае перезаписи файла, он хранит в себе полные участки файла, которые нужно записать обратно. Например, если мы хотим записать новые данные (`new_data`) начиная с 10 байта (`start`) длиной в 15 байтов (`length`), то в этот лог будут записаны байты с 10 по 15 из текущего, еще не измененного файла (`old_data`).

Алгоритм записи данных будет следующим:

1. `creat("/dir/undo.log")` - Создаем файл undo лога 
2. `write("/dir/undo.log", "[check_sum, start, length, old_data]")` - Записываем в него данные из исходного файла, которые собираемся изменить:
   - `start` - начальная позиция в файле, с которой собираемся производить запись
   - `length` - длина перезаписываемого участка
   - `old_data` - сами данные, которые перезаписываем (не новые, а старые для отката)
   - `check_sum` - чек-сумма, вычисленная для `start`, `length` и `old_data`
3. `fsync("/dir/undo.log")` - Сбрасываем данные файла на диск
4. `fsync("/dir")` - Сбрасываем содержимое директории (теперь undo лог точно на диске)
5. `write("/dir/data", start, new_data)` - Записываем новые данные (`start` - тот же самый, что и записали в `undo.log`)
6. `fsync("/dir/data")` - Сбрасываем изменения основного файла на диск
7. `unlink("/dir/undo.log")` - Удаляем undo лог
8. `fsync("/dir")` - Сбрасываем изменение данных директории на диск (удаление undo лога)
  
Что здесь учли:
- Отказ прямо после создания файла undo лога - в начале идет чек-сумма, с помощью которой можно это обнаружить (некоторые используют специальную константу, но чек-сумму и для этого можно использовать)
- Переупорядочивание операций записи - чек-сумма для всей записи в undo логе на случай, если операции записи будут переупорядочены (если изменения большие, то возможно одним `write` не обойтись) или нарушения целостности
- Отказ перед началом записи данных в сам файл - вызываем `fsync` для файла undo лога и самой директории, после этого можно быть уверенным, что файл на диске
- Удаление самого файла undo лога - в конце вызываем `fsync` для директории, чтобы undo лог был действительно удален, иначе могут возникнуть проблемы.

Как производить восстановление, наверное, стало понятно:
1. Проверяем наличие undo лога
   - Undo лог присутствует
   - Он не пуст
   - Все чек-суммы корректные
2. Если undo лог проверен:
   1. Применяем к целевому файлу хранящуюся откат (к самому файлу с данными - команду из undo лога)
   2. Сбрасываем изменения на диск (целевой файл)
   3. Удаляем undo лог
   4. Сбрасываем изменения на диск (директория)

Даже если в процессе восстановления произойдет сбой, то это не должно нарушить целостность данных, т.к. откат должен быть идемпотентен (стоит еще учитывать PSOW и атомарность сегмента, см. выше). 

Но удаление файла более затратная операция чем та же запись, т.к. надо обновить не только данные в директории, но и, возможно, пометить страницы чистыми для очищения места на диске. В SQLite используется журнал (undo лог) и в качестве оптимизации можно использовать 2 опции:
- Обрезать файл до 0 - `PRAGMA journal_mode=TRUNCATE`
- Занулять заголовок журнала - `PRAGMA journal_mode=PERSIST`

В [документации](https://devdoc.net/database/sqlite-3.0.7.2/atomiccommit.html#section_7_6) это описано.

<spoiler title="Undo лог в SQLite">

Раз уж разговор зашел о SQLite, то и в качестве примера приведу его. 
Сам процесс выполнения коммита описан на странице [Atomic Commit In SQLite](https://devdoc.net/database/sqlite-3.0.7.2/atomiccommit.html), но в коде реализован так:

```c++
// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/btree.c#L4388
int sqlite3BtreeCommit(Btree *p){
  int rc;
  // Первая фаза - создание журнала и запись данных в файл БД
  rc = sqlite3BtreeCommitPhaseOne(p, 0);
  if( rc==SQLITE_OK ){
    // Вторая фаза - удаление/обрезание/зануление журнала
    rc = sqlite3BtreeCommitPhaseTwo(p, 0);
  }
  return rc;
}

// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/btree.c#L4267
int sqlite3BtreeCommitPhaseOne(Btree *p, const char *zSuperJrnl){
  int rc = SQLITE_OK;
  if( p->inTrans==TRANS_WRITE ){
    rc = sqlite3PagerCommitPhaseOne(pBt->pPager, zSuperJrnl, 0);
  }
  return rc;
}

// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/pager.c#L6437
int sqlite3PagerCommitPhaseOne(
  Pager *pPager,                  /* Pager object */
  const char *zSuper,            /* If not NULL, the super-journal name */
  int noSync                      /* True to omit the xSync on the db file */
){
  int rc = SQLITE_OK;             /* Return code */

  if( 0==pagerFlushOnCommit(pPager, 1) ){
    // ...
  }else{
    if( pagerUseWal(pPager) ){
      // ...
    }else{
      // 1. Записываем страницы, которые хотим изменить в журнал
      rc = pager_incr_changecounter(pPager, 0);
      // 2. Сбрасываем журнал на диск
      rc = syncJournal(pPager, 0);
      if( rc!=SQLITE_OK ) goto commit_phase_one_exit;

      // 3. Записываем измененные страницы в сам файл БД
      pList = sqlite3PcacheDirtyList(pPager->pPCache);
      if( /* ... */ ){
        rc = pager_write_pagelist(pPager, pList);
      }
      if( rc!=SQLITE_OK ) goto commit_phase_one_exit;

      // 3. Сбрасываем данные файла БД на диск
      if( /* ... */ ){
        rc = sqlite3PagerSync(pPager, zSuper);
      }
    }
  }

commit_phase_one_exit:
  return rc;
}

// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/pager.c#L4259
static int syncJournal(Pager *pPager, int newHdr){
  int rc;                         /* Return code */

  if( /* ... */ ){
    if( /* ... */ ){
      if( /* ... */ ){
        // Вначале записываем количество страниц = 0, на всякий случай
        if( rc==SQLITE_OK && 0==memcmp(aMagic, aJournalMagic, 8) ){
          static const u8 zerobyte = 0;
          rc = sqlite3OsWrite(pPager->jfd, &zerobyte, 1, iNextHdrOffset);
        }

        if( /* ... */ ){
          // Сбрасываем записанные данные
          rc = sqlite3OsSync(pPager->jfd, pPager->syncFlags);
          if( rc!=SQLITE_OK ) return rc;
        }
        
        // Записываем сам заголовок
        rc = sqlite3OsWrite(
            pPager->jfd, zHeader, sizeof(zHeader), pPager->journalHdr
        );
        if( rc!=SQLITE_OK ) return rc;
      }
      if( /* ... */ ){
        // Еще раз сбрасываем содержимое файла на диск
        rc = sqlite3OsSync(pPager->jfd, pPager->syncFlags|
          (pPager->syncFlags==SQLITE_SYNC_FULL?SQLITE_SYNC_DATAONLY:0)
        );
        if( rc!=SQLITE_OK ) return rc;
      }
    }else{
      pPager->journalHdr = pPager->journalOff;
    }
  }
  
  return SQLITE_OK;
}

// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/pager.c#L6372
int sqlite3PagerSync(Pager *pPager, const char *zSuper){
  int rc = SQLITE_OK;
  if( /* ... */ ){
    // fsync
    rc = sqlite3OsSync(pPager->fd, pPager->syncFlags);
  }
  return rc;
}

// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/btree.c#L4356
int sqlite3BtreeCommitPhaseTwo(Btree *p, int bCleanup){
  if( p->inTrans==TRANS_WRITE ){
    int rc;
    rc = sqlite3PagerCommitPhaseTwo(p->pBt->pPager);
    if( rc!=SQLITE_OK && bCleanup==0 ){
      sqlite3BtreeLeave(p);
      return rc;
    }
  }
  
  return SQLITE_OK;
}

// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/pager.c#L6674
int sqlite3PagerCommitPhaseTwo(Pager *pPager){
  int rc = SQLITE_OK;                  /* Return code */
  rc = pager_end_transaction(pPager, pPager->setSuper, 1);
  return pager_error(pPager, rc);
}

// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/pager.c#L2033
static int pager_end_transaction(Pager *pPager, int hasSuper, int bCommit){
  int rc = SQLITE_OK;
  int rc2 = SQLITE_OK;     

  if( isOpen(pPager->jfd) ){
    if( sqlite3JournalIsInMemory(pPager->jfd) ){
      // ...
    }else if( pPager->journalMode==PAGER_JOURNALMODE_TRUNCATE ){
      // PRAGMA journal_mode=TRUNCATE
      // Обрезаем файл до 0
      if( pPager->journalOff==0 ){
        rc = SQLITE_OK;
      }else{
        rc = sqlite3OsTruncate(pPager->jfd, 0);
        if( rc==SQLITE_OK && pPager->fullSync ){
          rc = sqlite3OsSync(pPager->jfd, pPager->syncFlags);
        }
      }
      pPager->journalOff = 0;
    }else if( pPager->journalMode==PAGER_JOURNALMODE_PERSIST){
      // PRAGMA journal_mode=PERSIST
      // Зануляем заголовок журнала
      rc = zeroJournalHdr(pPager, hasSuper||pPager->tempFile);
      pPager->journalOff = 0;
    }else{
      // PRAGMA journal_mode=DELETE
      // Удаляем файл журнала
      int bDelete = !pPager->tempFile;
      sqlite3OsClose(pPager->jfd);
      if( bDelete ){
        rc = sqlite3OsDelete(pPager->pVfs, pPager->zJournal, pPager->extraSync);
      }
    }
  }
  // ...
  return (rc==SQLITE_OK?rc2:rc);
}

// Реализация sqlite3OsDelete для Unix систем 
// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/os_unix.c#L6533
static int unixDelete(
  sqlite3_vfs *NotUsed,     /* VFS containing this as the xDelete method */
  const char *zPath,        /* Name of file to be deleted */
  int dirSync               /* If true, fsync() directory after deleting file */
){
  int rc = SQLITE_OK;
  
  // Удаляем файл - unlink
  if( osUnlink(zPath)==(-1) ){
    rc = unixLogError(SQLITE_IOERR_DELETE, "unlink", zPath);
    return rc;
  }
  
  // Обновляем содержимое директории
  if( (dirSync & 1)!=0 ){
    int fd;
    rc = osOpenDirectory(zPath, &fd);
    if( rc==SQLITE_OK ){
      // Улучшенный "fsync"
      if( full_fsync(fd,0,0) ){
        rc = unixLogError(SQLITE_IOERR_DIR_FSYNC, "fsync", zPath);
      }
    }else{
      rc = SQLITE_OK;
    }
  }
  return rc;
}

// Улучшенная версия fsync, которая учитывает особенности и баги некоторых систем
// https://github.com/sqlite/sqlite/blob/5007833f5f82d33c95f44c65fc46221de1c5950f/src/os_unix.c#L3638
static int full_fsync(int fd, int fullSync, int dataOnly){
  int rc;

  /* If we compiled with the SQLITE_NO_SYNC flag, then syncing is a
  ** no-op.  But go ahead and call fstat() to validate the file
  ** descriptor as we need a method to provoke a failure during
  ** coverage testing.
  */
#ifdef SQLITE_NO_SYNC
  {
    struct stat buf;
    rc = osFstat(fd, &buf);
  }
#elif HAVE_FULLFSYNC
  if( fullSync ){
    rc = osFcntl(fd, F_FULLFSYNC, 0);
  }else{
    rc = 1;
  }
  /* If the FULLFSYNC failed, fall back to attempting an fsync().
  ** It shouldn't be possible for fullfsync to fail on the local
  ** file system (on OSX), so failure indicates that FULLFSYNC
  ** isn't supported for this file system. So, attempt an fsync
  ** and (for now) ignore the overhead of a superfluous fcntl call.
  ** It'd be better to detect fullfsync support once and avoid
  ** the fcntl call every time sync is called.
  */
  if( rc ) rc = fsync(fd);

#elif defined(__APPLE__)
  /* fdatasync() on HFS+ doesn't yet flush the file size if it changed correctly
  ** so currently we default to the macro that redefines fdatasync to fsync
  */
  rc = fsync(fd);
#else
  rc = fdatasync(fd);
#if OS_VXWORKS
  if( rc==-1 && errno==ENOTSUP ){
    rc = fsync(fd);
  }
#endif /* OS_VXWORKS */
#endif /* ifdef SQLITE_NO_SYNC elif HAVE_FULLFSYNC */

  if( OS_VXWORKS && rc!= -1 ){
    rc = 0;
  }
  return rc;
}
```

</spoiler>

#### Redo лог

Redo лог работает по аналогичной схеме. Разница в том, что записываем в этот лог не старые, а новые данные, и после выполнения операции не удаляем:

1. `creat("/dir/redo.log")` - Создаем файл redo лога
2. `write("/dir/redo.log", "[check_sum, start, length, new_data]")` - Записываем в него данные из исходного файла, которые собираемся изменить:
  - `start` - начальная позиция в файле, с которой собираемся производить запись
  - `length` - длина перезаписываемого участка
  - `new_data` - новые данные, которые хотим записать
  - `check_sum` - чек-сумма, вычисленная для `start`, `length` и `new_data`
3. `fsync("/dir/redo.log")` - Сбрасываем данные файла на диск
4. `fsync("/dir")` - Сбрасываем содержимое директории (теперь redo лог точно на диске)
5. `write("/dir/data", start, new_data)` - Записываем новые данные (`start` - тот же самый, что и записали в `redo.log`)
6. `fsync("/dir/data")` - Сбрасываем изменения основного файла на диск
7. `unlink("/dir/redo.log")` - Удаляем redo лог
8. `fsync("/dir")` - Сбрасываем изменение данных директории на диск (удаление redo лога)

Преимуществом redo лога можно считать, то, что ответ о завершенной операции пользователю можно отправлять как только мы успешно сделали запись в redo логе (шаг 5), т.к. изменения либо будут применены сейчас, либо при восстановлении после перезагрузки.

Стоит также заметить, что постоянное создание и удаление файлов - операция затратная. Для оптимизации, можно просто добавлять новые записи, которые потом надо будет просто применить при перезагрузке. Нужно операции коммитить, чтобы лишний раз их не применять. Закоммитить записи можно с помощью с помощью специальной записи, которая добавляется в конец лога (получается каждая запись в логе может быть либо операцией, либо коммитом). Также можно специальным образом изменить заголовок записи операции, например, хранить флаг коммита и занулять его при завершении операции или занулять заголовок, как бы говоря, что операция применена (как в SQLite при `PRAGMA journal_mode=PERSIST`). Вот мы и пришли к WAL - Write Ahead Log.

<spoiler title="WAL в Postgres">

Redo лог часто встречается в базах данных. Он есть в [Oracle](https://docs.oracle.com/en/database/oracle/oracle-database/19/admin/managing-the-redo-log.html), [MySQL](https://dev.mysql.com/doc/refman/8.0/en/innodb-redo-log.html), [Postgres](https://www.postgresql.org/docs/current/runtime-config-wal.html) или [SQLite](https://www.sqlite.org/wal.html). Поэтому в качестве примера, рассмотрим как происходит работа с WAL в Postgres.

Пример разделен на 2 части:
1. Вызов `COMMIT` - сохранение данных в WAL
2. Сброс грязных страниц на диск (вытеснение) - сброс данных в файл БД

Уже здесь мы можем увидеть то самое преимущество WAL: достаточно сделать единственную запись (в redo лог), вернуть ответ пользователю и продолжить работать с грязными страницами в памяти. Сама грязная страница будет сброшена в файл БД потом, например, при очередном запуске чекпоинтера или при восстановлении после сбоя.

```c++
// 1. Вызов COMMIT

// https://github.com/postgres/postgres/blob/cc6e64afda530576d83e331365d36c758495a7cd/src/backend/access/transam/xact.c#L2158
static void
CommitTransaction(void)
{   
    // ...
	RecordTransactionCommit();
    // ...
}

// https://github.com/postgres/postgres/blob/97d85be365443eb4bf84373a7468624762382059/src/backend/access/transam/xact.c#L1284
static TransactionId
RecordTransactionCommit(void)
{
    // ...
    XactLogCommitRecord(GetCurrentTransactionStopTimestamp(),
                        nchildren, children, nrels, rels,
                        ndroppedstats, droppedstats,
                        nmsgs, invalMessages,
                        RelcacheInitFileInval,
                        MyXactFlags,
                        InvalidTransactionId, NULL /* plain commit */ );
    // ...
}

// https://github.com/postgres/postgres/blob/eeefd4280f6e5167d70efabb89586b7d38922d95/src/backend/access/transam/xact.c#L5736
XLogRecPtr
XactLogCommitRecord(TimestampTz commit_time,
					int nsubxacts, TransactionId *subxacts,
					int nrels, RelFileLocator *rels,
					int ndroppedstats, xl_xact_stats_item *droppedstats,
					int nmsgs, SharedInvalidationMessage *msgs,
					bool relcacheInval,
					int xactflags, TransactionId twophase_xid,
					const char *twophase_gid)
{
    // ...
	return XLogInsert(RM_XACT_ID, info);
}

XLogRecPtr
XLogInsertRecord(XLogRecData *rdata,
				 XLogRecPtr fpw_lsn,
				 uint8 flags,
				 int num_fpi,
				 bool topxid_included)
{
    // ...
    XLogFlush(EndPos);
    // ...
}

// https://github.com/postgres/postgres/blob/dbfc44716596073b99e093a04e29e774a518f520/src/backend/access/transam/xlog.c#L2728
void
XLogFlush(XLogRecPtr record)
{
	// ...
	XLogWrite(WriteRqst, insertTLI, false);
    // ...
}

// https://github.com/postgres/postgres/blob/dbfc44716596073b99e093a04e29e774a518f520/src/backend/access/transam/xlog.c#L2273
static void
XLogWrite(XLogwrtRqst WriteRqst, TimeLineID tli, bool flexible)
{
	while (/* Есть данные для записи */)
	{
	    // 1. Создаем новый сегмент лога, либо открываем текущий
	    if (/* Размер сегмента превышен */)
		{
			openLogFile = XLogFileInit(openLogSegNo, tli);
		}
		if (openLogFile < 0)
		{
			openLogFile = XLogFileOpen(openLogSegNo, tli);
		}

        // 2. Выполняем непосредственную запись страниц WAL лога
        do
        {
            written = pg_pwrite(openLogFile, from, nleft, startoffset);
        } while (/* Есть данные для записи */);
        
        // 3. Сбрасываем содержимое файла на диск (fsync)
        if (finishing_seg)
        {
            issue_xlog_fsync(openLogFile, openLogSegNo, tli);
        }
    }
}

// https://github.com/postgres/postgres/blob/dbfc44716596073b99e093a04e29e774a518f520/src/backend/access/transam/xlog.c#L8516
void
issue_xlog_fsync(int fd, XLogSegNo segno, TimeLineID tli)
{
    // Вызов fsync в зависимости от конфигурации
	switch (wal_sync_method)
	{
		case WAL_SYNC_METHOD_FSYNC:
		    pg_fsync_no_writethrough(fd);
			break;
		case WAL_SYNC_METHOD_FSYNC_WRITETHROUGH:
		    pg_fsync_writethrough(fd);
			break;
		case WAL_SYNC_METHOD_FDATASYNC:
		    pg_fdatasync(fd);    
			break;
		// ...
	}
}

// 2. Сброс измененных страниц на диск, в файл БД

// https://github.com/postgres/postgres/blob/97d85be365443eb4bf84373a7468624762382059/src/backend/storage/buffer/bufmgr.c#L3437
static void
FlushBuffer(BufferDesc *buf, SMgrRelation reln, IOObject io_object,
			IOContext io_context)
{
    // 3. Сбрасываем буфер в WAL (если еще нет)
	if (buf_state & BM_PERMANENT)
		XLogFlush(recptr);
    
    // Сбрасываем изменения на диск, в файл БД (пишем и сбрасываем, соответствующие шаги дальше)
    smgrwrite(reln,
			  BufTagGetForkNum(&buf->tag),
			  buf->tag.blockNum,
			  bufToWrite,
			  false);
}

// https://github.com/postgres/postgres/blob/eeefd4280f6e5167d70efabb89586b7d38922d95/src/include/storage/smgr.h#L121
static inline void
smgrwrite(SMgrRelation reln, ForkNumber forknum, BlockNumber blocknum,
		  const void *buffer, bool skipFsync)
{
	smgrwritev(reln, forknum, blocknum, &buffer, 1, skipFsync);
}

// https://github.com/postgres/postgres/blob/eeefd4280f6e5167d70efabb89586b7d38922d95/src/backend/storage/smgr/smgr.c#L631
void
smgrwritev(SMgrRelation reln, ForkNumber forknum, BlockNumber blocknum,
		   const void **buffers, BlockNumber nblocks, bool skipFsync)
{
	smgrsw[reln->smgr_which].smgr_writev(reln, forknum, blocknum,
										 buffers, nblocks, skipFsync);
}

// https://github.com/postgres/postgres/blob/eeefd4280f6e5167d70efabb89586b7d38922d95/src/backend/storage/smgr/md.c#L928
void
mdwritev(SMgrRelation reln, ForkNumber forknum, BlockNumber blocknum,
		 const void **buffers, BlockNumber nblocks, bool skipFsync)
{
    // 5. Записываем данные в файл БД
	while (/* Есть еще блоки для записи */)
	{
        FileWriteV(v->mdfd_vfd, iov, iovcnt, seekpos,
                   WAIT_EVENT_DATA_FILE_WRITE);
	}
	
	// 6. Сбрасываем данные на диск (файл БД)
    register_dirty_segment(reln, forknum, v);
}


// https://github.com/postgres/postgres/blob/6b41ef03306f50602f68593d562cd73d5e39a9b9/src/backend/storage/file/fd.c#L2192
ssize_t
FileWriteV(File file, const struct iovec *iov, int iovcnt, off_t offset,
		   uint32 wait_event_info)
{
	returnCode = pg_pwritev(vfdP->fd, iov, iovcnt, offset);	
}

// https://github.com/postgres/postgres/blob/eeefd4280f6e5167d70efabb89586b7d38922d95/src/backend/storage/smgr/md.c#L1353
static void
register_dirty_segment(SMgrRelation reln, ForkNumber forknum, MdfdVec *seg)
{
	if (/* Не удалось зарегистрировать fsync запрос для чекпоинтера */)
	{
		FileSync(seg->mdfd_vfd, WAIT_EVENT_DATA_FILE_SYNC);
	}
}

// https://github.com/postgres/postgres/blob/c20d90a41ca869f9c6dd4058ad1c7f5c9ee9d912/src/backend/storage/file/fd.c#L2297
int
FileSync(File file, uint32 wait_event_info)
{
	pg_fsync(VfdCache[file].fd);
}
```

</spoiler>

## Сегментированный лог + снапшот

Раз поговорили про WAL, то стоит и рассказать про сегментированный лог. [Сегментированный лог](https://martinfowler.com/articles/patterns-of-distributed-systems/segmented-log.html) - это паттерн представления единого "логического" лога в виде нескольких "физических" сегментов:
- Раньше - единственный жирный файл, который хранил в себе все.
- Теперь - множество небольших (в сравнении в единственным файлом) сегментов.

Но сам по себе сегментированный лог имеет не так много преимуществ, в сравнении с единственным файлом. Вот тут приходит снапшот - слепок состояния приложения, к которому применили определенные команды из лога. Вот эта иллюстрация из статьи про Raft визуализирует отношения между логом и снапшотом:

#### TODO: иллюстрация 12 из In Search Of Understandable Consesnsus

В результате, у нас есть 2 "файла", в которых хранится состояние всего приложения:
- Снапшот - файл с состоянием приложения после применения определенного количества команд, и
- Сегментированный лог - множество сегментов (файлов), представляющих последовательность команд, которые необходимо применить к снапшоту

Состояние приложения получаем как: "Снапшот" + команды из "Сегментированный лог" 

И теперь магия - с каждым типом файлов мы умеем работать:
- Для создания или обновления снапшота применяем `Atomic Create Via Rename` или `Atomic Replace Via Rename` соответственно
- Когда поступает команда для изменения состояния - записываем ее в наш лог (запись в redo лог обсуждалась)
- Создание новых сегментов лога - через `Atomic Create Via Rename`

Если у нас не сегментированный лог, а _монолог_ (название придумал сам), то освобождение места (удаление примененных команд или их сжатие) будет затруднительно, т.к. отказоустойчивое изменение потребует копирования всех записей из лога и последующего `ARVR`. При большом размере лога, эта операция может занять много времени и ресурсов, но, в случае с сегментированным, достаточно просто удалить/сжать покрываемые снапшотом сегменты. 

Некоторые примеры:
- Postgres: WAL представляется в виде нескольких [сегментов](https://habr.com/ru/companies/otus/articles/717684/)
- Apache Kafka: Каждая партиция состоит из нескольких [сегментов](https://kafka.apache.org/documentation/#log)
- etcd: Данные хранит в [снапшоте](https://pkg.go.dev/github.com/etcd-io/etcd/snap) и [сегментированном WAL](https://pkg.go.dev/go.etcd.io/etcd/wal)
- log-cabin: Данные хранятся в [снапшоте](https://github.com/logcabin/logcabin/blob/master/Storage/SnapshotFile.cc) и [сегментированном WAL](https://github.com/logcabin/logcabin/blob/master/Storage/SegmentedLog.cc)

Подобный подход к организации хранения данных имеет множество преимуществ:
- Отказоустойчивое обновление состояния приложения: отказоустойчивая запись в WAL и обновление снапшота (примеры выше)
- Скорость применения операций увеличивается: запись в WAL происходит в конец файла - локальность данных и минимальное (потенциальное) время позиционирования головки
- Репликация производится эффективнее: потоковая репликация состояния (команды в WAL идут строго последовательно) или, при необходимости, можем отправить весь снапшот сразу
- Скорость запуска возрастает: вместо чтения всего лога, загружаем снапшот и применяем только необходимые команды

Также, поверх WAL возможно реализовать транзакции, как в Postgres.

# Заключение

Все началось со статьи [Files Are Hard](https://danluu.com/file-consistency/). Тогда я понял зачем нужны чек-суммы, что данные после записи надо сбрасывать (`fsync`) и, вообще, файлы не так просты. После, я решил копнуть поглубже и понял насколько глубока эта кроличья нора. Писать закончил с трудом, т.к. постоянно хотелось поискать еще каких-нибудь исследований, найти неизвестные факты и т.д. Главный вывод из всей статьи - файловая абстракция протекает. Если приложение работает с файлами и при этом данные важны, то необходимо понимать как устроена файловая запись и паттерны для работы с ней. В противном случае, вероятность потери данных или нарушения логики работы системы/приложения резко возрастают.

Я прошелся по основным слоям, которые проходит запрос записи, но некоторые темы не покрыты:
- Поведение сетевых файловых систем
- Описание устройства накопителей данных
- Устройство и реализация файловых систем
- Сравнение различных файловых систем
- Кроссплатформенность (точнее говоря, каких гарантий можно достичь на различных платформах - ОС, рантайм, железо)
- Баги в реализациях (они есть, например, TODO: ссылки на баги)
- Поведение внутри эмуляторов (Cygwin, WSL) или виртуальных машин (VMWare, VirtualBox)

Надеюсь, статья была полезна. Полезные ссылки прилагаю:

Файловые системы:
- [Files Are Hard](https://danluu.com/file-consistency/)
- [Filesystem Error Handling](https://danluu.com/filesystem-errors/)
- [All File Systems Are Not Created Equal: On the Complexity of Crafting Crash-Consistent Applications](https://www.usenix.org/system/files/conference/osdi14/osdi14-paper-pillai.pdf)
- [Can Applications Recover from fsync Failures?](https://www.usenix.org/system/files/atc20-rebello.pdf)
- [Ensuring data reaches disk](https://lwn.net/Articles/457667/)
- [Specifying and Checking File System Crash-Consistency Models](https://www.cs.utexas.edu/~bornholt/papers/ferrite-asplos16.pdf)
- [Model-Based Failure Analysis of Journaling File Systems](https://research.cs.wisc.edu/wind/Publications/sfa-dsn05.pdf)
- [EIO: Error Handling is Occasionally Correct](https://www.usenix.org/legacy/event/fast08/tech/full_papers/gunawi/gunawi.pdf)

Накопители:
- [Storage subsystem performance: analysis and recipes](https://gudok.xyz/sspar/)
- [The Life and Death of SSDs and HDDs: Similarities, Differences, and Prediction Models](https://arxiv.org/pdf/2012.12373.pdf)
- [Large Scale Studies of Memory, Storage, and Network Failures in a Modern Data Center](https://arxiv.org/pdf/1901.03401.pdf)
- [Investigating Power Outage Effects on Reliability of Solid-State Drives](https://arxiv.org/pdf/1805.00140.pdf)
- [Backblaze Drive Stats for 2023](https://www.backblaze.com/blog/backblaze-drive-stats-for-2023/)
