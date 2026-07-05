/*
 * libsigsys2enosys.c — freestanding aarch64 LD_PRELOAD shim.
 *
 * Android's untrusted_app seccomp filter (SECCOMP_RET_TRAP) raises SIGSYS on
 * syscalls it doesn't allowlist — notably clone3, which musl/glibc try FIRST and
 * expect to fall back from on ENOSYS. A plain Linux kernel returns -ENOSYS for an
 * unknown syscall; Android's filter kills instead. This shim reinstates the
 * ENOSYS contract: catch SIGSYS, make the trapped syscall look like it returned
 * -ENOSYS, and resume — so musl's clone3->clone (and similar) fallbacks work and
 * the Claude Code musl binary runs inside the app sandbox.
 *
 * LD_PRELOAD'd into the musl-loaded claude process. It MUST NOT depend on bionic
 * libc (that would collide with the process's musl), so: no libc calls, raw
 * syscalls only, zero DT_NEEDED. Bionic headers are used for struct LAYOUT only
 * (ucontext_t/siginfo_t are kernel-defined; the kernel fills them) — not linked.
 *
 * Build: see build-sigsys.sh (NDK clang, --target=aarch64-linux-android, -nostdlib).
 */

#include <signal.h>
#include <ucontext.h>
#include <stdint.h>

#define SC_SIGSYS     31
#define SC_SA_SIGINFO 4
#define SC_ENOSYS     38
#define SC_NR_write   64
#define SC_NR_rt_sigaction 134

/* Raw syscalls — no libc. */
static long sc4(long n, long a, long b, long c, long d) {
    register long x8 __asm__("x8") = n;
    register long x0 __asm__("x0") = a;
    register long x1 __asm__("x1") = b;
    register long x2 __asm__("x2") = c;
    register long x3 __asm__("x3") = d;
    __asm__ volatile("svc #0"
                     : "+r"(x0)
                     : "r"(x8), "r"(x1), "r"(x2), "r"(x3)
                     : "memory", "cc");
    return x0;
}

/* Minimal "sigsys2enosys: converted syscall N\n" to stderr, no libc. */
static void log_syscall(int nr) {
    char buf[48];
    const char *p = "sigsys2enosys: converted syscall ";
    int i = 0;
    while (*p) buf[i++] = *p++;
    if (nr < 0) buf[i++] = '-', nr = -nr;
    char num[12]; int j = 0;
    if (nr == 0) num[j++] = '0';
    while (nr > 0) { num[j++] = (char)('0' + nr % 10); nr /= 10; }
    while (j > 0) buf[i++] = num[--j];
    buf[i++] = '\n';
    sc4(SC_NR_write, 2, (long)buf, i, 0);
}

/* Kernel sigaction struct for the raw rt_sigaction syscall (differs from the
 * userspace struct sigaction — no libc translation layer here). */
struct k_sigaction {
    void (*handler)(int, siginfo_t *, void *);
    unsigned long flags;
    void (*restorer)(void);   /* unset -> kernel uses the vDSO sigreturn trampoline */
    unsigned long mask;       /* kernel sigset, 8 bytes on aarch64 */
};

static void sigsys_handler(int sig, siginfo_t *si, void *ucv) {
    (void)sig;
    ucontext_t *uc = (ucontext_t *)ucv;
    log_syscall(si->si_syscall);
    /* Make the trapped syscall "return" -ENOSYS. On aarch64 the saved PC already
     * points to the instruction AFTER the svc (ELR = svc+4), so do NOT advance it
     * — only overwrite the result register x0. */
    uc->uc_mcontext.regs[0] = (unsigned long)(-SC_ENOSYS);
}

__attribute__((constructor))
static void install(void) {
    struct k_sigaction act = {0};
    act.handler = sigsys_handler;
    act.flags = SC_SA_SIGINFO;
    sc4(SC_NR_rt_sigaction, SC_SIGSYS, (long)&act, 0, 8 /* sizeof kernel sigset */);
}
