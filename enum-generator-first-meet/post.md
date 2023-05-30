# enum class и мой первый раз (использования Source Generator)

## Содержание 

1. Что такое `enum class`
2. Возможная реализация в C#
3. Чем помогут Source Generators
4. Основные моменты использования
5. Дробление на несколько проектов
6. Добавляем вспомогательные проекты (JsonConverter)
7. Подводные камни
8. Тесты
9. Выводы
10. Полезные ссылки

## Что такое `enum class`

В Kotlin существует тип `enum class`. 
По факту, это тот же `enum`, но с несколькими возможностями:
1. Переопределение общих методов (`toString`, `getHashCode`)
2. Реализация специфичных для конкретного значения функций
3. Поддержка корректности типов на уровне компилятора

Представим, что у нас есть такой `enum class`

```kotlin
interface IWorker {
    fun doWork()
}

enum class Role: IWorker {
    Admin {
        override fun toString(): String {
            return "Sysadmin"
        }

        override fun doWork() {
            println("Интенсивно работаю")
        }
    },
    User {
        override fun doWork() {
            println("Ставлю лайки")
        }
    },
    Moderator {
        override fun doWork() {
            println("Пишем больше постов")
        }
    }
}
```

Вот как можно было бы его использовать
```kotlin
val role = getRole()

println(
    when (role) {
        Role.Admin -> "Адыхаю"
        Role.User -> {
            role.doWork()
            "Работа закончена"
        }
        Role.Moderator -> "Работы пока нет"
    }
)
```


# Возможная реализация на C#

Как можно было бы реализовать на C#

```csharp
public interface IWorker
{
    void DoWork();
}


public abstract class Role : IWorker
{
    public static AdminRole Admin = new();
    public static UserRole User = new();
    public static ModeratorRole Moderator = new();

    public abstract void DoWork();

    public class AdminRole : Role
    {
        public override void DoWork()
        {
            Console.WriteLine("Интенсивно работаю");
        }

        public override string ToString()
        {
            return "Sysadmin";
        }
    }

    public class UserRole : Role
    {
        public override void DoWork()
        {
            Console.WriteLine("Ставлю лайки");
        }
    }

    public class ModeratorRole : Role
    {
        public override void DoWork()
        {
            Console.WriteLine("Пишем больше постов");
        }
    }
}
```

И использование было бы соответствующим:
```csharp
var role = GetRole();
Console.WriteLine(role switch
{
    Role.AdminRole admin => "Адыхаю",
    Role.UserRole user => DoWorkUser(user),
    Role.ModeratorRole moderator => "Работы пока нет"
});

string DoWorkUser(Role.UserRole userRole)
{
    userRole.DoWork();
    return "Работа закончена";
}
```

Для мимикрирования схожей функциональности потребовалось много кода.
Причем, часть функциональности, как например статический (ошибки компиляции) анализ, просто так не реализовать.

Можно заметить, что большая часть кода — _шаблонная_. 
Это прекрасная возможность для использования генераторов исходного кода.


Я давно хотел применить их на практике, поэтому первый проект, который я решил реализовать — 
генератор `enum class`'ов по `enum` из C#. 
Для работы я решил использовать инкрементальные генераторы, а не старые. 

Выбор пал в основном из-за производительности.

## Мой путь ~~самурая~~ создания генератора

Создание генератора не такая сложная вещь, 
в первую очередь благодаря большому количеству туториалов и примеров.

Алгоритм работы выглядит следующим образом:
1. Найти все необходимые перечисления
2. Вычленить из них необходимую информацию: название, неймспейс, значения
3. Сгенерировать из этих значений `enum class`

Звучит просто (и на деле так).

**Найти все необходимые перечисления**

Частой практикой является использование маркерных атрибутов. 
Их добавление может быть реализовано несколькими способами:
1. Создать из генератора при старте
2. Сделать отдельным пакетом
3. Добавить вместе с генератором

Вначале, я выбрал первую стратегию — добавлял вместе генератором.
Это самый простой путь и реализуется в несколько строк.

```csharp
context.RegisterPostInitializationOutput(ctx =>
        {
            var sourceCode = @"using System;

namespace EnumClass
{
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false)]
    internal class EnumClassAttribute: Attribute
    { }
}";
            ctx.AddSource("EnumClassAttribute.g.cs", sourceCode);
        });
```


Здесь возникает основная проблема — конфликты при компиляции, так как этот атрибут может объявляться в нескольких сборках с одним и тем же именем.

Можно сделать этот атрибут `internal`, но в игру вступает `[InternalsVisibleTo]`, который вы, скорее всего, для тестов, не так ли?

Поэтому я выбрал 3 стратегию: добавлять его отдельной сборкой. 
Второй пункт я не рассматривал — никто не хочет лишний раз двигаться.

Это сделать не так уж и трудно. 
Просто нужно немного знать MSBuild и компоновку NuGet пакета.

Добавление сборки в NuGet пакет можно реализовать 3 строчками в `.csproj` генератора.

```xml
<ItemGroup>
    <!-- Добавляем ссылку на проект с атрибутами к генератору -->
    <ProjectReference Include="..\EnumClass.Attributes\EnumClass.Attributes.csproj" PrivateAssets="All" />
    
    <!-- Сгенерированный dll в NuGet пакет -->
    <None Include="$(OutputPath)\EnumClass.Attributes.dll" PackagePath="lib/netstandard2.0" Visible="true" Pack="true" />
</ItemGroup>
```

Немного пояснений:
- `Include="$(OutputPath)\EnumClass.Attributes.dll"` - Какой dll после сборки копировать
- `PackagePath="lib/netstandard2.0"` - Куда копировать эту сборку. Библиотеки, доступные для пользователей, необходимо добавлять по этому пути.
- `Visible="true"` - Эта сборка видна потребителям
- `Pack="true"` - Включать в NuGet пакет

В общем-то все. Теперь атрибуты доступны пользователям при подключении


**Вычленить необходимую информацию**

Есть 2 способа получения необходимой информации: синтаксис и контекст компиляции.

Синтаксис, в сравнении с компиляцией, обладает преимуществами:
- Производительнее — можно получать только необходимые изменения и игнорировать остальные
- Обновления в реальном времени — мы подписываемся и код обновляется тут же, без необходимости перекомпиляции.

Для проекта генератора я выбрал путь синтаксиса. 
Но этого недостаточно, поэтому после получения всех синтаксических узлов перечислений, 
я получал из них `INamedTypeSymbol`. 

Грубо говоря, `INamedTypeSymbol` это удобное для работы представление всех именованных типов,
которые есть в системе: классы, структуры, перечисления.

На мой взгляд, работать с этими представлениями удобнее, так как они могут дать больше информации.

Логику генерации можно выразить так:

```csharp
// 1. Подписываемся на изменения перечисления
IncrementalValuesProvider<EnumDeclarationSyntax> enums = 
    generatorContext
       .SyntaxProvider
       .CreateSyntaxProvider(
            predicate: (node, _) => node is EnumDeclarationSyntax {AttributeLists.Count: > 0}, 
            transform: GetSemanticModelForEnumClass)
       .Where(x => x is not null)!;

// 2. Регистрируем обработчик 
var provider = generatorContext.CompilationProvider.Combine(enums.Collect());
        generatorContext.RegisterSourceOutput(provider, (context, tuple) => GenerateAllEnumClasses(tuple.Left, tuple.Right, context));

// 3. Итерируемся по всем синтаксическим узлам опредения перечисления и получаем необходимую информацию
// enums - список всех полученных от провайдера синтаксических узлов
foreach (var syntax in enums)
{
    // Получаем семантическую модель
    // Грубо говоря, 
    var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
    
    // Получаем из модели INamedTypeSymbol
    if (semanticModel.GetDeclaredSymbol(syntax) is not { EnumUnderlyingType: not null } enumSymbol)
    {
        continue;
    }
    
    // Фабричный метод вычленения информации из перечисления
    var enumInfo = EnumInfoFactory.CreateFromNamedTypeSymbol(enumSymbol, enumClassAttributeSymbol, enumMemberInfoAttribute!);
    
    // Сохраняем информацию в список
    enumInfos.Add(enumInfo);
}
```

**Сгенерировать из этих значений `enum class`**

Генерировать код можно 2 способами: добавлять файлы с исходным кодом и добавлять синтаксические деревья напрямую.

Я выбрал первый способ - создание файлов с исходным кодом.
Другой способ я не использовал ввиду его низкой (как говорят) производительности.

Для генерации я использую старый добрый `StringBuilder`. 
Весь код генерируется простым добавлением форматированного кода.

Например, вот добавление определения класса
```csharp
builder.AppendFormat("{2} abstract partial class {0}: "
                   + "IEquatable<{0}>, IEquatable<{1}>, "
                   + "IComparable<{0}>, IComparable<{1}>, IComparable\n", 
                   enumInfo.ClassName, enumInfo.FullyQualifiedEnumName, 
                   enumInfo.Accessibility.Keyword);
```

**Исправленный генератором пример**

Изначально мы написали `enum class` своими руками, но _красиво_ это не получилось.
Теперь попробуем сделать эту работу через генератор.

```csharp
public interface IWorker
{
    void DoWork();
}

[EnumClass]
public enum Role
{
    [EnumMemberInfo(StringValue = "Sysadmin")]
    Admin,
    User,
    Moderator
}

namespace EnumClass
{
    public partial class Role: IWorker
    {
        public abstract void DoWork();

        public partial class AdminEnumValue
        {
            public override void DoWork()
            {
                Console.WriteLine("Интенсивно работаю");
            }

            public string GetGreeting() => "Привет, я админ";
        }

        public partial class UserEnumValue
        {
            public override void DoWork()
            {
                Console.WriteLine("Ставлю лайки");
            }
        }

        public partial class ModeratorEnumValue
        {
            public override void DoWork()
            {
                Console.WriteLine("Пишем больше постов");
            }
        }
    }
}
```

Работа с ним ведется так
```csharp
var role = GetRole();

Console.WriteLine(
    role.Switch(
        admin => admin.GetGreeting(),
        user =>
        {
            user.DoWork();
            return "Работа закончена";
        },
        moderator => "Работы пока нет")
    );
```

Лаконично и красиво!

О другой функциональности можно узнать заглянув в проект: 
[примеры](https://github.com/ashenBlade/EnumClass/tree/master/samples), 
[README](https://github.com/ashenBlade/EnumClass/blob/master/README.md), 
[тесты](https://github.com/ashenBlade/EnumClass/tree/master/tests)

## Дробление на несколько проектов

Изначально вся разработка велась в единственном проекте — там и генератор, и бизнес логика, и все все все.
Но со временем становилось всё сложнее работать с возрастающим количеством файлов в проекте.

Поэтому я принял решение разбить 1 проект на 2: генератор и бизнес-логика.
Это оказалось не сложнее, чем добавить проект с атрибутами. 
Разница только в способе добавления сборки в пакет.

Проект я разбил на 2:
- [`EnumClass.Generator`](https://github.com/ashenBlade/EnumClass/tree/master/src/EnumClass.Generator) - сам генератор
- [`EnumClass.Core`](https://github.com/ashenBlade/EnumClass/tree/master/src/EnumClass.Core) - проект с бизнес-логикой

Добавляется проект с бизнес-логикой так

```xml
<ItemGroup>
    <ProjectReference Include="..\EnumClass.Core\EnumClass.Core.csproj" OutputItemType="Analyzer" PrivateAssets="all" />
    <None Include="$(OutputPath)\EnumClass.Core.dll" PackagePath="analyzers/dotnet/cs" Visible="false" Pack="true" />
</ItemGroup>
```

Как можно заметить `PackagePath` поменялся на `analyzers/dotnet/cs`. По этому пути находятся все сборки, необходимые для генераторов и сам генератор.
Также, если генератор зависит от какой-то сборки, то при добавлении ссылки необходимо указать `OutputItemType="Analyzer"`. 
Точно так же, как и для самого генератора.

Файл проекта генератора со всеми ссылками можно найти [здесь](https://github.com/ashenBlade/EnumClass/blob/master/src/EnumClass.Generator/EnumClass.Generator.csproj)

После такого разбиения, проект с генератором стал состоять из единственного класса - самого генератора.

> Подключение зависимостей

Подключение проекта с генератором тоже стоит упомянуть.
При подключении NuGet пакета никаких дополнительных действий выполнять не надо.
Если настроили сборку пакета правильно, то все должно заработать.
 
Другое дело когда проект генератора подключается напрямую:
1. Проект с генератором должен быть помечен атрибутами `OutputItemType="Analyzer"` и `ReferenceOutputAssembly="false"`
```xml
<ProjectReference Include="..\..\src\EnumClass.Generator\EnumClass.Generator.csproj" ReferenceOutputAssembly="false" OutputItemType="Analyzer" />
```
2. Проект с бизнес-логикой также должен быть помечен ими
```xml
<ProjectReference Include="..\..\src\EnumClass.Core\EnumClass.Core.csproj" ReferenceOutputAssembly="false" OutputItemType="Analyzer" />
```
3. Проект с атрибутами должен быть добавлен как обычный проект
```xml
<ProjectReference Include="..\..\src\EnumClass.Attributes\EnumClass.Attributes.csproj" />
```

## Добавляем вспомогательные проекты (JsonConverter)

Библиотека мало кого может заинтересовать, если для ее использования нужно много стараться.
Поэтому, создание экосистемы вокруг нее жизненно необходимо.

Стоит заметить, что вся бизнес-логика содержится в проекте `EnumClass.Core`, 
а сам генератор теперь — _тонкий клиент_.

То есть нам ничего не мешает создать _новый генератор_, 
но уже _для сгенерированного класса_.

Идея для нового проекта долго не искалась - json (де)сериализатор.

`.csproj` для нового проекта не отличалась от основного генератора, только не содержит ссылки на сборку с атрибутами.

Основная проблема — нахождение всех необходимых перечислений. 
Я предположил, что, скорее всего, `enum` будет использоваться в проекте с доменными сущностями, 
а сериализация — это уже деятельность на границе с внешним миром.
Значит, требовать подключения этого генератора к тому же самому проекту — неправильно.

С решением этой проблемы мне помог [этот ответ на вопрос](https://stackoverflow.com/a/74163439/14109140) на Stack Overflow.

Всё решается использованием провайдера компиляции. 
Он позволяет получить ссылки на ссылающиеся сборки, а после на типы (символы) внутри них.

При запуске компиляции:

1. Получаем все сборки

```csharp
// Ссылки
ImmutableArray<IAssemblySymbol> assemblySymbols = compilation
                                                    .SourceModule
                                                    .ReferencedAssemblySymbols;
// Текущая
IAssemblySymbol currentAssemblySymbol = compilation.Assembly;
```

2. Получаем все неймспейсы из каждой сборки
```csharp
foreach (var assemblySymbol in assemblySymbols)
{
    foreach (var @namespace in GetAllNamespaces(assemblySymbol.GlobalNamespace))
    {
        // Обарбатываем неймспейс
    }
}

IEnumerable<INamespaceSymbol> GetAllNamespaces(INamespaceSymbol root)
{
    yield return root;
    foreach (var child in root.GetNamespaceMembers())
    {
        foreach (var next in GetAllNamespaces(child))
        {
            yield return next;
        }
    }
}
```
3. Получаем все перечисления из каждого неймспейса
```csharp

foreach (var member in @namespace.GetTypeMembers())
{
    foreach (var childOrSelf in GetAllNestedTypesAndSelf(member))
    {
        if (childOrSelf.TypeKind is TypeKind.Enum)
        {
            yield return childOrSelf;
        }            
    }
}

IEnumerable<INamedTypeSymbol> GetAllNestedTypesAndSelf(INamedTypeSymbol namedTypeSymbol)
{
    yield return namedTypeSymbol;
    
    if (namedTypeSymbol.GetTypeMembers() is { Length:>0 } namedTypeSymbols)
    {
        foreach (var member in namedTypeSymbols)
        {
            foreach (var type in GetAllNestedTypesAndSelf(member))
            {
                yield return type;
            }
        }
    }
}
```

4. Проверяем наличие необходимого атрибута
```csharp
foreach (var namedTypeSymbol in FactoryHelpers.ExtractAllEnumsFromCompilation(compilation))
{
    if (IsMarkedWithEnumClassAttribute(namedTypeSymbol))
    {
        parsed.Add(CreateFromNamedTypeSymbol(namedTypeSymbol, enumClassAttribute, enumMemberInfoAttribute));
    }
}

// Это локальная функция и переменная attribute (символ маркерного атрибута) передается аргументом внешеней
bool IsMarkedWithEnumClassAttribute(INamedTypeSymbol enumTypeSymbol)
{
    foreach (var attribute in enumTypeSymbol.GetAttributes())
    {
        if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, enumClassAttribute))
        {
            return true;
        }
    }

    return false;
}
```

Примеры использования `EnumClass` можно проекта можно найти в папке [`samples`](https://github.com/ashenBlade/EnumClass/tree/master/samples)

## Кэш

В процессе работы, я столкнулся с одной серьёзной проблемой - кэш:
1. IDE все подсвечивает красным, а `dotnet run` запускается без ошибок
2. `dotnet run` не запускается, но через IDE можно
3. Все запускается, но нужная функциональность не добавлялась, сколько бы я не пересобирал проекты

**IDE подсвечивает, но все запускается**

Это проблема кэша IDE. Решается инвалидацией кэша.

В Rider это делается через `File->Invalidate Caches...`

После этого IDE перезагружается и все должно заработать.

**`dotnet run` не запускается, но через IDE можно**

На самом деле в проекте содержатся ошибки и запуститься он не может.
Запускаются уже собранные проекты (старые, в `bin`)

**Всё запускается, но нужная функциональность не добавляется**

Когда я подключал проекты, то решил попробовать собрать NuGet пакет и подключить его, а не проект.
Дело в том, что когда подключаешь пакеты из локальных папок (по умолчанию ищутся в `packages` в корне проекта), 
то эти пакеты кэшируются в локальный кэш NuGet, то есть копируются.

И получается, что запускается только старый проект раз за разом.

Единственное решение — удалить эти пакеты из кэша. 

Для своих пакетов я использовал команду
```shell
rm -r ~/.nuget/packages/enumclass*
```

После этого нужно восстановить зависимости - `dotnet restore`. 
Новые версии локальных пакетов должны подтянуться.

**Если ничего не помогает**

Бывало так, что ничего не помогало.
В такие моменты помогала старая добрая перезагрузка ПК.

Часто это было решением проблем.

## Советы

Хочу дать несколько полезных советов по использованию API.

**Составление имен типов**

Часто нужно получить имя других типов. 
Например, если мы создаем маппер, то чтобы инстанциировать новый экземпляр DTO.

Но возникает вопрос: как на него сослаться?
- Только название класса нельзя — могут быть коллизии
- Добавлять новые `using` порой бывает накладно (или просто лень)

Ответ: `ToDisplayString()`

У всех, реализующих интерфейс `ISymbol`, есть метод `ToDisplayString()`. 
Именно он поможет вам получить необходимое название типа.

На вход он также принимает `SymbolDisplayFormat` - указание того, в каком формате мы хотим это название получить.

- `ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` вернет полностью квалифицированное имя. 
Оно будет начинаться с корня всего проекта и начинаться с `global::`.
Его можно будет использовать в любом контексте и быть уверенным, что компилятор поймет именно нужный тип.

    Пример: `global::EnumClass.SimpleEnum.PetKind`

- `ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);` вернет короткое имя.
Это имя часто зависит от контекста. Например, при применении к классу.

    Пример: `PetKind`

**Сравнение символов**

Как минимум, для поиска маркерных атрибутов придется сравнивать `INamedTypeSymbol`.
В самом простом случае это делается сравнением полей `Name`. 
Но что если названия просто совпадают, а найденный тип из другой библиотеки?

Правильно производить сравнение символов, используя собственный компаратор - `SymbolEqualityComparer`.

У него есть 2 реализации:
- `SymbolEqualityComparer.Default` - реализация, использующая `IComparer<T>`
- `SymbolEqualityComparer.IncludeNullability` - реализация, учитывающая nullability ссылочных типов

Примеры:
- Проверка маркерного атрибута:
```csharp
bool IsMarkedWithEnumClassAttribute(INamedTypeSymbol enumTypeSymbol)
{
    foreach (var attribute in enumTypeSymbol.GetAttributes())
    {
        if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, enumClassAttribute))
        {
            return true;
        }
    }

    return false;
}
```

- Хранение кэша обработанных типов из других сборок
```csharp
var processedAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
var foundEnumSymbols = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        
foundEnumSymbols.UnionWith(ExtractEnumsFromAssembly(compilation.Assembly));
processedAssemblies.Add(compilation.Assembly);

foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
{
    var foundEnums = ExtractEnumsFromAssembly(assemblySymbol);
    foundEnumSymbols.UnionWith(foundEnums);
    processedAssemblies.Add(assemblySymbol);
}
```

## Тесты

Одно из самых важных мест - тестирование. 
С генераторами можно проводить несколько видов тестирования. 


**Интеграционное тестирование**

Под этим тестированием я понимаю проверку работы уже сгенерированного кода.

Для меня это основной метод тестирования:
> Не важно как красиво я описал интерфейсы - 
> если генерируется неправильный код, то работа не сделана.

С точки зрения _написания_ тестов — это один из самых простых способов. 
Мы пишем буквально только юнит тесты, разница только в том, что тесты на сгенерированный код.

Типовой тест класс выглядит так:
```csharp
public class ToStringTests
{
    // Объявляем класс для генерации
    [EnumClass]
    public enum PunctuationMark
    {
        [EnumMemberInfo(StringValue = ".")]
        Dot,
        [EnumMemberInfo(StringValue = ",")]
        Comma,
        [EnumMemberInfo(StringValue = "!")]
        Exclamation,
        [EnumMemberInfo(StringValue = "?")]
        Question,
    }
    
    // Ожидаемое поведение
    public static IEnumerable<object> PunctuationMarkWithString => new[]
    {
        new object[] {EnumClass.PunctuationMark.Dot, "."},
        new object[] {EnumClass.PunctuationMark.Comma, ","},
        new object[] {EnumClass.PunctuationMark.Exclamation, "!"},
        new object[] {EnumClass.PunctuationMark.Question, "?"},
    };
    
    // Пишем тест
    [Theory]
    [MemberData(nameof(PunctuationMarkWithString))]
    public void ToString__WithStringValueAttribute__ShouldReturnSpecifiedValue(EnumClass.PunctuationMark mark, string expected)
    {
        var actual = mark.ToString();
        Assert.Equal(expected, actual);
    }
}
```

> P.S. Не всегда подобное тестирование проще, например, если создается стаб для запросов, то
> интеграционное будет сложнее.

**Юнит тестирование**

Юнит тестирование в данном контексте может означать 2 вещи:
- Тестирование бизнес-логики
- Тестирование работы генератора

Один из полезных побочных эффектов разделения одной сборки на 2 - можно отдельно протестировать бизнес логику.

В таких тестах самое сложное - подготовка: 
- Написать _строки_ с исходным кодом
- Подключить зависимости до необходимых сборок
- Указать необходимые настройки компиляции

Хорошо, что код подготовки также шаблонен и его можно вынести в отдельные функции. 
Как например:
```csharp
private Compilation Compile(params string[] sourceCodes)
{
    var compilation = CSharpCompilation.Create("Test", 
        // Создаем синтаксические деревья из переденных строк исходного кода
        syntaxTrees: sourceCodes.Select(x => CSharpSyntaxTree.ParseText(x)),
        // Добавляем ссылки на зависимые сборки
        references: new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(EnumClassAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(EnumInfo).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.GetCallingAssembly().Location),
            MetadataReference.CreateFromFile(typeof(string).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Runtime")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("netstandard")).Location),
        }, 
        // Компилируем как библиотеку, а не исполняемый файл
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    
    // Натравливаем генератор на полученную компиляцию
    CSharpGeneratorDriver.Create(new EnumClassIncrementalGenerator())
                         .RunGeneratorsAndUpdateCompilation(compilation, out var resultCompilation, out var diagnostics);
    
    // Возвращаем обновленный код
    return resultCompilation;
}
```

После из этой компиляции я получаю всю необходимую информацию о перечислениях.
Например
```csharp
[Fact]
public void GetAllEnumsFromCompilation__WithSingleMarkedEnum__ShouldReturnListWithSingleElement()
{
    // Пишем исходный код
    var sampleEnumCode = @"using EnumClass.Attributes;

namespace Test;

[EnumClass]
public enum SampleEnum
{
    One = 1,
    Two = 2,
    Three = 3
}";
    // Компилируем его
    var compilation = Compile(sampleEnumCode);
    
    // Получаем все необходимые перечисления
    var enums = EnumInfoFactory.GetAllEnumInfosFromCompilation(resultCompilation, new SourceProductionContext())!;
    
    // Проверяем корректность
    Assert.Single(enums);
}
```

Генераторы также могут уведомлять пользователя об исключительных ситуациях или неожиданном поведении.
Для взаимодействия с пользователями используются сообщения диагностики.

Такое тоже можно проверить - в результате вызова `.RunGeneratorsAndUpdateCompilation(compilation, out var resultCompilation, out ImmutableArray<Diagnostic> diagnostics)` 
вторым `out` параметром мы получаем список всех диагностик, которые случились за время компиляции.

Если вы объявляете свои собственные сообщения, то можно проверять их.
Я пока проверяю успешную компиляцию - отсутствие ошибок во время копиляции:
```csharp
[Fact]
public void WithSingleMember__ShouldGenerateWithoutErrors()
{
    // Здесь компиляция и запуск генератора...
    
    Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
}
```

**Снапшот тестирование**

Snapshot тестирование — это тестирование путём сравнения полученного результата с образцом. 
Чаще встречается в UI тестировании, но узнал о нём при работе с генераторами.
Описание этого типа тестирования можно найти [здесь](https://github.com/aqarain/snapshot-testing-guide).

Для снапшот тестирования (не только генераторов) в C# есть библиотека [`Verify`](https://github.com/VerifyTests/Verify)

Лично мой опыт такого тестирования — негативный.
После запуска тестов много изменений в файлах, которые нужно проверить:
- Переменное количество членов перечисления
- Добавление новой функциональности
- Изменение названий некоторых переменных

После нескольких запусков тестов, 
где после каждого я по несколько минут сидел и принимал изменения,
решил от них отказаться.

Скорее всего это связано со спецификой моей бизнес-логики, 
а для вас подойдёт.

## Выводы

Генераторы исходного кода — мощная вещь. 
Давно хотел их попробовать и вот появилась мотивация.

Главная трудность, с которой я столкнулся, — небольшое количество примеров и документации, 
либо есть, но обсуждаются очень простые случаи. 
Надеюсь в будущем это изменится и вокруг генераторов появится более развитая экосистема.

## Полезные ссылки

- Основные концепции API генераторов (Syntax Node, Semantic Model, Symbol) - [https://github.com/bodziosamolot/sourceGenerators/blob/main/notes.md](https://github.com/bodziosamolot/sourceGenerators/blob/main/notes.md)
- Серия статей, покрывающих основные моменты процесса создания генератора, - [https://andrewlock.net/series/creating-a-source-generator/](https://andrewlock.net/series/creating-a-source-generator/)
- Список проектов с генераторами - [https://github.com/amis92/csharp-source-generators](https://github.com/amis92/csharp-source-generators)
- Визуализация синтаксического дерева - [https://sharplab.io/](https://sharplab.io/) или плагин для Rider [https://plugins.jetbrains.com/plugin/16902-rossynt](https://plugins.jetbrains.com/plugin/16902-rossynt)
- Конечно же, мой генератор - [https://github.com/ashenBlade/EnumClass](https://github.com/ashenBlade/EnumClass)
