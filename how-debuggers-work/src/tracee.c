#include <stdio.h>
#include <stdlib.h>

long sum(long a, long b) {
    long c = a - b;
    return c;
}

int main(int argc, char **argv) {
    long left = atol(argv[0]);
    long right = atol(argv[1]);
    long result = sum(left, right);
    printf("%ld\n", result);
    return 0;
}