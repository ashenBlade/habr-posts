"""
Скрипт для создания графика сравнения производительности реализаций очередей
"""

import pandas as pd
import matplotlib.pyplot as plt
from matplotlib.pyplot import Axes, Figure
from dataclasses import dataclass
from enum import Enum
import datetime


def parse_duration(duration: str) -> float:
    """Спарсить длительность из таблицы в микросекунды"""
    [number, time] = duration.split(' ')
    number: str
    number = number.replace(',', '')
    return float(number) * (1 if time == 'ms' else 1000)


class WorkloadType(str, Enum):
    EnqueueDequeue = 'EnqueueDequeue'
    Uniform = 'Uniform'


WORKLOAD_TYPE = WorkloadType.Uniform


def plot_concurrent(uniform, ed):
    def run_group(plot: Axes, work_type: WorkloadType):
        workload = csv[(csv.WorkloadType == work_type)]
        groups = workload.groupby(['QueueParameters'])
        for (key, value) in groups:
            x, y = [], []
            for r in value.itertuples():
                x.append(r.ThreadsCount)
                y.append(parse_duration(r.Mean))
            threshold, height = key[0].split(', ')
            plot.plot(x, y, label=f'Конкурентная{threshold}, {height}')

    csv = pd.read_csv('data/ConcurrentPriorityQueue.Benchmarks.ConcurrentPriorityQueueBenchmark-report.csv', delimiter=';')
    run_group(uniform, WorkloadType.Uniform)
    run_group(ed, WorkloadType.EnqueueDequeue)


def plot_synchronous(uniform, ed):
    def run_group(plot: Axes, work_type: WorkloadType):
        workload = csv[(csv.WorkloadType == work_type)]
        x, y = [], []
        for r in workload.itertuples():
            x.append(r.ThreadsCount)
            y.append(parse_duration(r.Mean))
        plot.plot(x, y, label=f'Блокирующая')
        plot.set_xticks(x)
    csv = pd.read_csv('data/ConcurrentPriorityQueue.Benchmarks.LockingPriorityQueueBenchmark-report.csv', delimiter=';')
    run_group(uniform, WorkloadType.Uniform)
    run_group(ed, WorkloadType.EnqueueDequeue)


def main():
    fig, axs = plt.subplots(2, 1)
    fig: Figure
    uniform_ax: Axes
    ed_ax: Axes
    uniform_ax, ed_ax = axs[0], axs[1]

    plot_concurrent(uniform_ax, ed_ax)
    plot_synchronous(uniform_ax, ed_ax)

    uniform_ax.set_xlabel('Количество потоков')
    uniform_ax.set_ylabel('Длительность, мс')
    uniform_ax.set_title('Равномерная нагрузка')
    uniform_ax.legend()

    ed_ax.set_xlabel('Количество потоков')
    ed_ax.set_ylabel('Длительность, мс')
    ed_ax.set_title('Сначала вставка, потом добавление')
    ed_ax.legend()

    fig.set_figwidth(10)
    fig.set_figheight(10)
    fig.tight_layout()

    fig.show()




if __name__ == '__main__':
    main()
