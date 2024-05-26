#include <sys/ptrace.h>
#include <wait.h>
#include <sys/user.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <sys/types.h>
#include <signal.h>
#include <errno.h>

static void tracee_main(int argc, char **argv)
{
    if (ptrace(PTRACE_TRACEME, 0, NULL, NULL) == -1)
    {
        perror("ptrace");
        exit(EXIT_FAILURE);
    }

    execvp(argv[1], argv + 1);
    perror("execvp");
    exit(EXIT_FAILURE);
}

static int perform_action(pid_t child_pid)
{
    struct user_regs_struct regs;
    if (ptrace(PTRACE_GETREGS, child_pid, NULL, &regs) == -1)
    {
        perror("ptrace");
        return -1;
    }

    // TODO: поменять rax и rbx на другое что-нибудь
    if (regs.rax == 0)
    {
        void *addr = (void *)regs.rbx;
        void *data;
        if (ptrace(PTRACE_PEEKTEXT, child_pid, addr, (void *)&data) == -1)
        {
            perror("ptrace");
            return -1;
        }

        *((int *)&data) = 1;
        if (ptrace(PTRACE_POKETEXT, child_pid, addr, (void *)&data) == -1)
        {
            perror("ptrace");
            return -1;
        }
    }

    return 0;
}

static void tracer_main(pid_t child_pid)
{
    int wstatus;
    while (1)
    {
        /*
         * Ждем остановки tracee
         */
        if (waitpid(child_pid, &wstatus, 0) == -1)
        {
            perror("waitpid");
            break;
        }

        if (!WIFSTOPPED(wstatus))
        {
            /*
             * Процесс завершил работу
             */
            exit(EXIT_SUCCESS);
        }

        if (perform_action(child_pid) == -1)
        {
            break;
        }

        if (ptrace(PTRACE_CONT, child_pid, NULL, NULL) == -1)
        {
            break;
        }
    }

    if (kill(SIGKILL, child_pid) == -1 && errno != ESRCH)
    {
        perror("kill");
        exit(EXIT_FAILURE);
    }

    if (waitpid(child_pid, &wstatus, 0) == -1)
    {
        perror("waitpid");
        exit(EXIT_FAILURE);
    }

    exit(EXIT_FAILURE);
}

int main(int argc, char **argv)
{
    if (argc == 1)
    {
        printf("Usage: %s PROGRAM [ARGS]\n", argv[0]);
        exit(1);
    }

    pid_t pid = fork();
    if (pid == -1)
    {
        perror("fork");
        return 1;
    }

    if (pid == 0)
    {
        tracee_main(argc, argv);
        return 1;
    }
    else
    {
        tracer_main(pid);
        return 0;
    }
}