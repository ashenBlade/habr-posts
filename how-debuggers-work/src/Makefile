debugger: debugger.c
	gcc -g -O0 debugger.c -o debugger
tracee: tracee.s
	gcc -g -O0 -no-pie tracee.s -o tracee

clean:
	rm -f debugger tracee

all: debugger tracee