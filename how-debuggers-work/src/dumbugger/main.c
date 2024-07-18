#include <sys/wait.h>
#include <stdio.h>
#include <stdlib.h>
#include <assert.h>
#include <sys/ptrace.h>
#include <string.h>
#include <sys/user.h>
#include <unistd.h>

static void tracee_main(int argc, const char **argv)
{
    assert(1 < argc);
    const char *progname = argv[1];
    assert(progname != NULL);

    if (ptrace(PTRACE_TRACEME, 0, NULL, NULL) == -1)
    {
        perror("ptrace");
        exit(1);
    }

    execvp(progname, (char *const *)argv + 1);
    perror("execvp");
    exit(1);
}

static void print_debugger_help()
{
    printf("List of commands:\n\n"
           "help\t- show this help message\n"
           "continue\t- continue execution of stopped process\n"
           "info regs\t - show information about registers\n");
}

static int show_register_info(pid_t child_pid)
{
    struct user_regs_struct regs;
    if (ptrace(PTRACE_GETREGS,  child_pid, NULL, &regs) == -1)
    {
        perror("ptrace");
        return -1;
    }

    printf("RAX = %lld\n"
           "RDX = %lld\n"
           "RSI = %lld\n",
           regs.rax, regs.rdx, regs.rsi);
    return 0;
}

static int set_register_command(pid_t child_pid, const char* cmd, int cmd_len)
{
    char reg[4];
    long value;
    if (sscanf(cmd + 4 /* skip "set " */,  "%3s %ld", reg,  &value)  !=  2)
    {
        printf("could not parse \"set\" command\n");
        return 0;
    }

    struct user_regs_struct regs;
    if (ptrace(PTRACE_GETREGS, child_pid, NULL, &regs)  ==  -1)
    {
        perror("ptrace");
        return -1;
    }

    if (strcmp(reg, "rax")  ==  0)
    {
        regs.rax = value;
    }
    else if (strcmp(reg, "rdx")   ==  0)
    {
        regs.rdx = value;
    }
    else if (strcmp(reg, "rsi")    ==  0)
    {
        regs.rsi = value;
    }
    else
    {
        printf("unknown register: %s\n", reg);
    }
    
    if (ptrace(PTRACE_SETREGS, child_pid, NULL, &regs)  ==  -1)
    {
        perror("ptrace");
        return -1;
    }
    return 0;
}

static int handle_user_commands(const pid_t child_pid, const int wstatus)
{
    char cmd[128];
    int cmd_len;

    while (1)
    {
        printf("> ");
        fflush(stdout);

        memset(cmd, 0, sizeof(cmd));
        if ((cmd_len = read(STDIN_FILENO, cmd, sizeof(cmd))) == -1)
        {
            perror("read");
            return -1;
        }

        if (cmd_len == 0)
        {
            /* Костыль */
            return -1;
        }

        /* \n -> \0 */
        cmd[cmd_len - 1] = '\0';
        if (strcmp(cmd, "continue") == 0)
        {
            break;
        }
        else if (strcmp(cmd, "help") == 0)
        {
            print_debugger_help();
        }
        else if (strcmp(cmd, "info regs") == 0)
        {
            if (show_register_info(child_pid) == -1)
            {
                return -1;
            }
        }
        else if (strncmp(cmd, "set", 3) == 0)
        {
            if (set_register_command(child_pid, cmd, cmd_len) == -1)
            {
                return -1;
            }
        }
        else
        {
            printf("Unknown command: %s\n", cmd);
        }
        
        printf("\n");
    }

    return 0;
}

static void debugger_main(pid_t child_pid)
{
    int wstatus;
    while (1)
    {
        if (waitpid(child_pid, &wstatus, 0) == -1)
        {
            perror("waitpid");
            exit(1);
        }

        if (!WIFSTOPPED(wstatus))
        {
            /*
             * Процесс завершил работу
             */
            break;
        }

        if (handle_user_commands(child_pid, wstatus) == -1)
        {
            exit(1);
        }

        if (ptrace(PTRACE_CONT, child_pid, NULL, NULL) == -1)
        {
            perror("ptrace");
            exit(1);
        }
    }

    printf("child exited with status: %d\n", WEXITSTATUS(wstatus));
    exit(0);
}

int main(int argc, const char **argv)
{
    if (argc < 2)
    {
        dprintf(STDERR_FILENO, "Usage: %s PROG_NAME [ PROG_ARGS ... e]\n", argv[0]);
        return 1;
    }

    pid_t child_pid = fork();
    if (child_pid == -1)
    {
        perror("fork");
        return 1;
    }

    if (child_pid == 0)
    {
        tracee_main(argc, argv);
    }
    else
    {
        debugger_main(child_pid);
    }

    return 1;
}