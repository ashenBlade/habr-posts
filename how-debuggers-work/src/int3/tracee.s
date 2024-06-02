.globl main

.section .data
# const char* message = ...
message: .asciz "%ld + %ld = %ld\n"

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

    # signal(SIGTRAP, sigtrap_handler)
    pushq %rdi
    pushq %rsi

    movl $5, %edi
    leaq sigtrap_handler(%rip), %rsi
    call signal

    popq %rsi
    popq %rdi
    
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

    # printf(message, left, right, sum)
    leaq message(%rip), %rdi
    popq %rcx # sum
    popq %rdx # right
    popq %rsi # left
    int $3 # interrupt
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

    movq %rbp, %rsp
    popq %rbp
    ret

sigtrap_handler:
    nop
    ret
