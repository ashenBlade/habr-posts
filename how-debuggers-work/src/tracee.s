.globl main

.data
# const char* message = ...
message: .asciz "%ld + %ld = %ld\n"

.text
# int main(int argc, const char** argv)
main:
    # Alignment 
    subq $8, %rsp
    
    pushq %rdi
    pushq %rsi

    movq $5, %rdi
    movq $1, %rsi
    movq $48, %rax
    syscall

    popq %rsi
    popq %rdi

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

    movq 16(%rsp), %rdi
    movq 24(%rsp), %rsi
    call sum

    popq %rdi
    popq %rsi

    pushq %rax # sum

    # raise(SIGCHLD)

    # In signal handler, stack will look like this
    # | ..... |
    # |-------|
    # | left  |
    # |-------| <-------- %rsp - 24
    # | right |
    # |-------| <-------- %rsp - 16
    # |  sum  |
    # |-------| <-------- %rsp - 8
    # | %rdi  |
    # |-------| <-------- %rsp
    # 
    # We use SIGCHLD, because it ignored by default

    pushq %rdi
    movq $17, %rdi
    call raise
    popq %rdi

    # printf(message, left, right, result)
    leaq message(%rip), %rdi
    popq %rcx # sum
    popq %rdx # right
    popq %rsi # left
    call printf

    # return 0
    addq $8, %rsp
    movq $0, %rax
    ret

main_error_exit:
    addq $8, %rsp
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

    movq %rbp, %rsp
    popq %rbp
    ret
