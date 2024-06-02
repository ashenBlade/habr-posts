#include <sys/ptrace.h>
#include <sys/types.h>
#include <sys/user.h>
#include <sys/wait.h>
#include <wait.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <signal.h>
#include <errno.h>
#include <string.h>
#include <fcntl.h>

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

static const char *rbp_addr_filename = "./rbp_addr_test";

static int
obtain_rbp_address(long *addr)
{
    int fd = open(rbp_addr_filename, O_RDONLY);
    if (fd == -1)
    {
        if (errno == ENOENT)
        {
            return -1;
        }

        perror("open");
        exit(1);
    }

    *addr = 0;
    if (read(fd, (void *)addr, sizeof(long)) == -1)
    {
        perror("read");
        exit(1);
    }

    if (close(fd) == -1)
    {
        perror("close");
        exit(1);
    }

    if (unlink(rbp_addr_filename) == -1)
    {
        perror("unlink");
        exit(1);
    }

    return 0;
}

static int perform_action(pid_t child_pid)
{
    /*
     * Stack:
     * | ....  <--- %rbp
     * | left  <--- %rbp - 8
     * | right <--- %rbp - 16
     * | sum   <--- %rbp - 24
     */
    long rbp_addr;
    if (obtain_rbp_address(&rbp_addr) == -1)
    {
        return 0;
    }

    long right_addr = rbp_addr - 16;
    errno = 0;
    long right_data = ptrace(PTRACE_PEEKDATA, child_pid, (void *)right_addr, NULL);
    if (errno != 0)
    {
        perror("ptrace(1)");
        return -1;
    }
    
    if (right_data != 3)
    {
        return 0;
    }

    right_data = 0;

    if (ptrace(PTRACE_POKEDATA, child_pid, (void *)right_addr, (void *)right_data) == -1)
    {
        perror("ptrace(2)");
        return -1;
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
            perror("ptrace");
            break;
        }
    }

    if (kill(child_pid, SIGKILL) == -1 && errno != ESRCH)
    {
        perror("kill");
        exit(EXIT_FAILURE);
    }

    if (waitpid(child_pid, &wstatus, 0) == -1)
    {
        perror("waitpid");
    }

    exit(EXIT_FAILURE);
}

int main(int argc, char **argv)
{
    if (argc == 1)
    {
        printf("Usage: %s PROGRAM [ARGS ...]\n", argv[0]);
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
    }
    else
    {
        tracer_main(pid);
    }

    return 1;
}