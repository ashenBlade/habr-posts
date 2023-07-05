Содержание:
1. ValueTask до и после .NET Core 2.1 - добавление IValueTaskSource
2. Устройство и алгоритм работы IValueTaskSource
3. Делаем полностью своими руками
4. Добавляем ManualResetValueTaskSource
5. Как реализован сокет с помощью него (все делают пример на нем, я не исключение)
6. Бенчмарки?
7. Ссылки

# ValueTask до и после .NET Core 2.1

Для оптимизации памяти был добавлен ValueTask.
Но жадным программистам и этого оказалось мало. 
Добавили IValueTaskSource
Новый конструктор

# Устройство и алгоритм работы IValueTaskSource

Определение интерфейса.
За что каждый метод отвечает.
Алгоритм их работы (последовательность вызова из ValueTask).
Добавить ссылку на [гитхаб](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Sources/IValueTaskSource.cs) IValueTaskSource 

# Делаем полностью своими руками

Постановка задачи `PcMonitor`.
Алгоритм работы с `PcMonitor`.

Создание класса 

# Добавляем ManualResetValueTaskSource

# Добавляем пулинг + почему нельзя заново await'ить

# Как реализован сокет с помощью него (все делают пример на нем, я не исключение)

# Бенчмарки?

