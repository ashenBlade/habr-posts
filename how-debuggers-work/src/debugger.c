#include <stdlib.h>
#include <stdio.h>

#include <unistd.h>
#include <errno.h>
#include <sys/ptrace.h>
#include <sys/wait.h>
#include <sys/user.h>

static int child_main() {
    const char *argv[] = {"./tracee", "1", "2", NULL};
    if (ptrace(PTRACE_TRACEME, 0, 0, 0) == -1) {
        perror("ptrace(PTRACE_TRACEME)");
        exit(1);
    }
    execve(argv[0], (char * const*) argv, NULL);
    perror("execve");
    exit(1);
}

int main(int argc, const char **argv) {
    pid_t child_pid = fork();
    if (child_pid == -1) {
        exit(1);
    }

    if (child_pid == 0) {
        child_main();
        exit(1);
    }
    
    int status;
    if (waitpid(child_pid, &status, 0) == -1) {
        perror("waitpid");
        exit(2);
    }

    if (WIFEXITED(status) || WIFSIGNALED(status)) {
        printf("child existed with status code: %d", WEXITSTATUS(status));
        exit(2);
    }

    /* Адрес инструкции перед началом sub */
    // const long sum_addr = 0x401136;
    const long sum_addr = 0x401136;

    /* Выставляем точку останова в начале функции sum */
    long word = ptrace(PTRACE_PEEKDATA, child_pid, (void *)sum_addr, NULL);
    if (word == -1) {
        perror("ptrace(PTRACE_PEEKDATA)");
        exit(3);
    }

    long saved_instruction = word;
    word = (word & ~0xFF) | ((long) 0xCC);
    if (ptrace(PTRACE_POKEDATA, child_pid, (void*)sum_addr, word) == -1) {
        perror("ptrace(PTRACE_POKEDATA)");
        exit(3);
    }

    /* Продолжаем выполнение */
    if (ptrace(PTRACE_CONT, child_pid, 0, 0) == -1) {
        perror("ptrace(PTRACE_CONT)");
        exit(3);
    }

    /* Дожидаемся остановки */
    if (waitpid(child_pid, &status, 0) == -1) {
        perror("waitpid");
        exit(3);
    }

    if (WIFEXITED(status) || WIFSIGNALED(status)) {
        printf("child existed with status code: %d", WEXITSTATUS(status));
        exit(3);
    }

    struct user_regs_struct regs;
    if (ptrace(PTRACE_GETREGS, child_pid, NULL, &regs) == -1) {
        perror("ptrace(PTRACE_GETREGS)");
        exit(4);
    }

    printf("Before sum: RDI = %lld, RSI = %lld\n", regs.rdi, regs.rsi);
    
    /* Восстанавливаем инструкцию, откатываемся и делаем шаг */
    if (ptrace(PTRACE_POKEDATA, child_pid, sum_addr, saved_instruction) == -1) {
        perror("ptrace(PTRACE_POKEDATA)");
        exit(4);
    }

    if (ptrace(PTRACE_GETREGS, child_pid, NULL, &regs) == -1) {
        perror("ptrace(PTRACE_GETREGS)");
        exit(5);
    }

    regs.rip--;
    if (ptrace(PTRACE_SETREGS, child_pid, NULL, &regs) == -1) {
        perror("ptrace(PTRACE_SETREGS)");
        exit(5);
    }

    if (ptrace(PTRACE_SINGLESTEP, child_pid, 0, 0) == -1) {
        perror("ptrace(PTRACE_GETREGS)");
        exit(5);
    }

    if (waitpid(child_pid, &status, 0) == -1) {
        perror("waitpid");
        exit(6);
    }

    if (WIFEXITED(status) || WIFSIGNALED(status)) {
        printf("child exited with status code: %d", WEXITSTATUS(status));
        exit(4);
    }

    /* Читаем состояние после этой инструкции */
    if (ptrace(PTRACE_GETREGS, child_pid, NULL, &regs) == -1) {
        perror("ptrace(PTRACE_GETREGS)");
        exit(4);
    }

    printf("After sum: RDI = %lld, RSI = %lld\n", regs.rdi, regs.rsi);

    /* Выполняем до остановки */
    if (ptrace(PTRACE_CONT, child_pid, 0, 0) == -1) {
        perror("ptrace(PTRACE_CONT)");
        exit(5);
    }

    if (waitpid(child_pid, &status, 0) == -1) {
        perror("waitpid");
        exit(5);
    }

    if (!WIFEXITED(status)) {
        printf("Child is still running");
        exit(5);
    }

    return 0;
}