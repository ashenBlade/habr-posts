.globl main

.data
# const char* message = ...
message: .asciz "%ld + %ld = %ld\n"

.text
# int main(int argc, const char** argv)
main:
    # Alignment 
    subq $8, %rsp

    # \if (argc != 3) return 1;
    movq %rdi, %rax
    movq %rsi, %rbx
    cmpl $3, %edi
    jz args_passed
    addq $8, %rsp
    movq $1, %rax
    ret
    
args_passed:
    # long left = parse_number(argv, 1)
    pushq %rdi
    pushq %rsi
    movq %rsi, %rdi
    movq $1, %rsi
    call parse_number
    popq %rsi
    popq %rdi

    pushq %rax

    # long right = parse_number(argv, 2)
    pushq %rdi
    pushq %rsi
    movq %rsi, %rdi
    movq $2, %rsi
    call parse_number
    popq %rsi
    popq %rdi

    pushq %rax

    # long result = sum(left, right)
    pushq %rsi
    pushq %rdi

    movq 16(%rsp), %rdi
    movq 24(%rsp), %rsi
    call sum

    popq %rdi
    popq %rsi

    # printf(message, left, right, result)
    leaq message(%rip), %rdi
    popq %rsi 
    popq %rdx 
    movq %rax, %rcx 
    call printf

    int $3

    # return 0
    popq %rsi
    popq %rdi
    addq $8, %rsp
    movq $0, %rax
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
