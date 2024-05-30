.globl main

.section .data
# const char* message = ...
message: .asciz "%ld + %ld = %ld\n"
.balign 8
# const char *rbp_filename = ...
rbp_filename: .asciz "./rbp_addr_test"

.section .text

# int main(int argc, const char** argv)
main:
    pushq %rbp
    movq %rsp, %rbp

    # \if (argc != 3) return 1;
    movq %rdi, %rax
    movq %rsi, %rbx
    cmpl $3, %edi
    jnz main_error_exit
    
    # long left = parse_number(argv, 1)
    pushq %rdi
    pushq %rsi
    movq %rsi, %rdi
    movq $1, %rsi
    call parse_number
    popq %rsi
    popq %rdi

    pushq %rax # left

    # long right = parse_number(argv, 2)
    pushq %rdi
    pushq %rsi
    movq %rsi, %rdi
    movq $2, %rsi
    call parse_number
    popq %rsi
    popq %rdi

    pushq %rax # right

    # long result = sum(left, right)
    pushq %rsi
    pushq %rdi

    movq -8(%rbp), %rdi
    movq -16(%rbp), %rsi
    call sum

    popq %rdi
    popq %rsi

    pushq %rax # sum

    # raise(SIGCHLD)

    # In signal handler, stack will look like this
    # | ..... |
    # |-------| <-------- %rbp
    # | left  |
    # |-------| <-------- %rbp - 8
    # | right |
    # |-------| <-------- %rbp - 16
    # |  sum  |
    # |-------| <-------- %rbp - 24
    # | %rdi  |
    # |-------| <-------- %rbp - 32
    # 
    # We use SIGCHLD, because it ignored by default

    # dump_rbp_address(%rbp)
    pushq %rdi

    movq %rbp, %rdi
    call dump_rbp_address
    
    # raise(SIGCHLD)
    movq $17, %rdi
    call raise

    popq %rdi

    # printf(message, left, right, sum)
    leaq message(%rip), %rdi
    popq %rcx # sum
    popq %rdx # right
    popq %rsi # left
    call printf

    # return 0
    popq %rbp
    movq $0, %rax
    ret

main_error_exit:
    popq %rbp
    movq $1, %rax
    ret

# long sum(long left, long right)
sum:
    pushq %rbp
    movq %rsp, %rbp

    # return left + right
    addq %rdi, %rsi
    movq %rsi, %rax

    popq %rbp
    ret

# long parse_number(const char** argv, int index)
parse_number:
    pushq %rbp
    movq %rsp, %rbp

    pushq %rdi
    pushq %rsi
    
    # return atol(argv[index])
    movq (%rdi, %rsi, 8), %rdi
    call atol
    
    popq %rsi
    popq %rdi

    # Это зачем?
    movq %rbp, %rsp
    popq %rbp
    ret

# void dump_rbp_address(unsigned long saved_rbp)
dump_rbp_address:
    pushq %rbp
    movq %rsp, %rbp

    # long saved_rbp -8(%rbp)
    # int fd         -16(%rbp)
    subq $16, %rsp
    movq %rdi, -8(%rbp)

    # fd = open(rbp_filename, O_WRONLY | O_CREAT | O_TRUNC, S_IRUSR | S_IWUSR | S_IRGRP | S_IWGRP | S_IROTH)
    pushq %rdi
    pushq %rsi
    pushq %rdx

    movq $0, %rax
    leaq rbp_filename(%rip), %rdi
    movq $577, %rsi
    movq $436, %rdx
    call open
    movl %eax, -16(%rbp)

    popq %rdx
    popq %rsi
    popq %rdi

    # \if (fd == -1) exit(1)
    cmpl $-1, %eax
    jz dump_rbp_address_fail
    movl %eax, -16(%rbp)
    
    # \if (write(fd, (void*)&saved_rbp, sizeof(long)) == -1) exit(1)
    pushq %rdi
    pushq %rsi
    pushq %rdx

    movl -16(%rbp), %edi
    movq %rbp, %rsi
    subq $8, %rsi
    movq $8, %rdx
    call write
    cmpl $-1, %eax
    jz dump_rbp_address_fail

    popq %rdx
    popq %rsi
    popq %rdi

    # \if (close(fd) == -1) exit(1)
    movl -16(%rbp), %edi
    call close
    cmpl $-1, %eax
    jz dump_rbp_address_fail

    addq $16, %rsp
    movq %rbp, %rsp
    popq %rbp
    ret
    
    # exit(1)
dump_rbp_address_fail:
    movq $2, %rdi
    call exit
