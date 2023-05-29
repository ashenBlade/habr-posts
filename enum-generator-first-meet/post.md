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

```kotlin
enum class Role {
    Admin {
        override fun toString(): string {
            return "Sysadmin"
        }
    },
    User {
        fun calculateSomething(value: Int): Double {
            return value / 0.5 + 1
        }
    },
    Moderator
}
```


# Возможная реализация на C#

Как можно было бы реализовать на c#

```csharp
public abstract class Role
{
    public static AdminRole Admin = AdminRole.Instance;
    public static UserRole User = UserRole.Instance;
    public static ModeratorRole Moderator = ModeratorRole.Instance;

    public class AdminRole : Role
    {
        public static readonly AdminRole Instance = new();

        private AdminRole()
        { }

        public override string ToString()
        {
            return "Sysadmin";
        }
    }

    public class UserRole : Role
    {
        public static readonly UserRole Instance = new();
        private UserRole() { }

        public double CalculateSomething(int value)
        {
            return value / 0.5 + 1;
        }
    }

    public class ModeratorRole : Role
    {
        public static readonly ModeratorRole Instance = new();
        private ModeratorRole() { }
    }
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

> Сделать спойлером

Более детальную разницу между Синтаксисом, Символами и Семантической моделью можно найти в [заметках компилятора](https://github.com/bodziosamolot/sourceGenerators/blob/main/notes.md#semantic-model)

**Сгенерировать из этих значений `enum class`**

Генерировать код можно 2 способами: добавлять файлы с исходным кодом и добавлять синтаксические деревья напрямую.

Я выбрал первый способ - создание файлов с исходным кодом.
Другой способ я не использовал ввиду его низкой (как говорят) производительности.

Для генерации я использую старый добрый `StringBuilder`. 
Весь код генерируется простым добавлением форматированного кода.

Например, вот так добавляю определение нового класса
```csharp
builder.AppendFormat("{2} abstract partial class {0}: "
                   + "IEquatable<{0}>, IEquatable<{1}>, "
                   + "IComparable<{0}>, IComparable<{1}>, IComparable\n", 
                   enumInfo.ClassName, enumInfo.FullyQualifiedEnumName, 
                   enumInfo.Accessibility.Keyword);
```

## Дробление на несколько проектов

Изначально вся разработка велась в единственном проекте — там и генератор, и бизнес логика, и все все все.
Но со временем становилось всё сложнее работать с возрастающим количеством файлов в проекте.

Поэтому я принял решение разбить 1 проект на 2: генератор и бизнес-логика.
Это оказалось не сложнее, чем добавить проект с атрибутами. 
Разница только в пути добавления сборки в пакет.

Проект я разбил на 2:
- EnumClass.Generator - сам генератор
- EnumClass.Core - проект с бизнес-логикой

Добавляется проект с бизнес-логикой так

```xml
<ItemGroup>
    <ProjectReference Include="..\EnumClass.Core\EnumClass.Core.csproj" OutputItemType="Analyzer" PrivateAssets="all" />
    <None Include="$(OutputPath)\EnumClass.Core.dll" PackagePath="analyzers/dotnet/cs" Visible="false" Pack="true" />
</ItemGroup>
```

Как можно заметить `PackagePath` поменялся на "analyzers/dotnet/cs". По этому пути находятся все сборки, необходимые для генераторов и сам генератор.
Также, если генератор зависит от какой-то сборки, то при добавлении ссылки необходимо указать `OutputItemType="Analyzer"`. 
Точно так же, как и для самого генератора.

Файл проекта генератора со всеми ссылками можно найти [здесь](https://github.com/ashenBlade/EnumClass/blob/master/src/EnumClass.Generator/EnumClass.Generator.csproj)

После такого разбиения, проект с генератором стал состоять из единственного класса - самого генератора.

> Разница в подключении проектов таким образом

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
Он позволяет получить ссылки на ссылающиеся сборки, а после на типы внутри них.
При запуске компиляции:

1. Получаем все сборки

```csharp
// Получаем все ссылаемые сборки
ImmutableArray<IAssemblySymbol> assemblySymbols = compilation
                                                    .SourceModule
                                                    .ReferencedAssemblySymbols;
// Получаем текущую сборки
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
        // Отфильтровываем только перечисления
        if (childOrSelf.TypeKind is TypeKind.Enum)
        {
            yield return childOrSelf;
        }            
    }
}

IEnumerable<INamedTypeSymbol> GetAllNestedTypesAndSelf(INamedTypeSymbol namedTypeSymbol)
{
    yield return namedTypeSymbol;
    
    if (namedTypeSymbol.GetTypeMembers() is {Length:>0} namedTypeSymbols)
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



## Кэш

В процессе работы, я столкнулся с единственной, но очень большой, трудностью - кэш:
1. IDE все подсвечивает красным, а `dotnet run` запускается без ошибок
2. `dotnet run` не запускается, но через IDE можно
3. Все запускается, но нужная функциональность не добавлялась, сколько бы я не пересобирал проекты

**IDE подсвечивает, но все запускается**

Это проблема кэша самой IDE. Решается инвалидацией кэша.

В Rider это делается через `File->Invalidate Caches...`

После этого IDE перезагружается и все должно заработать.

**`dotnet run` не запускается, но через IDE можно**

На самом деле в проекте содержатся ошибки и он запуститься не может.
А запускаются уже собранные проекты (старые, в `bin`)

**Все запускается, но нужная функциональность не добавляется**

Когда я подключал проекты, то решил попробовать собрать NuGet пакет и подключить его, а не проект.
Дело в том, что когда подключаешь пакеты из локальных папок, то эти пакеты кэшируются в локальный кэш NuGet,
то есть копируются.

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
> Не важно как красиво я сделал интерфейс моего проекта с бизнес логикой - 
> если генерируется неправильный код, то работа не сделана

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

Снапшот тестирование - это тестирование сравнением с образцом. 
Чаще встречается в UI тестировании, но познакомился с ним при работе с генераторами.

> Ссылка на вики или типа того

Для снапшот тестирования в C# есть библиотека [`Verify`](https://github.com/VerifyTests/Verify)

> Как подключить в проекты с тестами

Лично мой опыт их использования — негативный.
После запуска тестов много изменений в файлах, которые нужно проверить:
- Переменное количество членов перечисления
- Добавление новой функциональности
- Изменение названий некоторых переменных

Скорее всего это связано со спецификой моей бизнес-логики.
Но после нескольких запусков тестов, 
где после каждого я по несколько минут сидел и принимал изменения,
решил от них отказаться.

## Выводы

Генераторы исходного кода — мощная вещь. 
Давно хотел их попробовать.

Главная трудность, с которой я столкнулся, — малое количество примеров и документации.
Надеюсь в будущем это изменится и вокруг генераторов появится более развитая экосистема.

## Полезные ссылки


