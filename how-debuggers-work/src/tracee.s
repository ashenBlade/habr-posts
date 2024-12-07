.globl main

.section .rodata
format_str: .asciz "%ld\n"

.section .text
sum:
    # return a - b
    subq %rsi, %rdi
    movq %rdi, %rax
    ret

main:
    pushq %rbp
    movq %rsp, %rbp

    # %r12 - argv
    # %r13 - left
    # %r14 - right

    mov %rsi, %r12

    # long left = atol(argv[1])
    movq 8(%rsi), %rdi
    call atol
    movq %rax, %r13

    # long right = atol(argv[2])
    movq 16(%r12), %rdi
    call atol
    movq %rax, %r14

    # long result = sum(left, right)
    movq %r13, %rdi
    movq %r14, %rsi
    call sum

    # printf("%ld\n", result)
    movq $format_str, %rdi
    movq %rax, %rsi
    call printf

    xor %rax, %rax
    movq %rbp, %rsp
    popq %rbp
    ret
