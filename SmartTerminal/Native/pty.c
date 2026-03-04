/*
 * libpty.c — Minimal PTY wrapper for Android.
 *
 * Provides forkpty(), read, write, resize, and process management
 * for use via P/Invoke from .NET MAUI.
 *
 * Build with Android NDK:
 *   $NDK/toolchains/llvm/prebuilt/linux-x86_64/bin/aarch64-linux-android26-clang \
 *       -shared -fPIC -o libpty.so pty.c
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <errno.h>
#include <fcntl.h>
#include <signal.h>
#include <termios.h>
#include <sys/ioctl.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <pty.h>

/*
 * pty_open: Allocate a PTY and fork a child process running the given shell.
 *
 * Returns 0 on success, -1 on failure.
 * On success, *out_master_fd = master side fd, *out_pid = child pid.
 */
int pty_open(int rows, int cols, const char *shell, int *out_master_fd, int *out_pid) {
    struct winsize ws;
    ws.ws_row = (unsigned short)rows;
    ws.ws_col = (unsigned short)cols;
    ws.ws_xpixel = 0;
    ws.ws_ypixel = 0;

    int master_fd;
    pid_t pid = forkpty(&master_fd, NULL, NULL, &ws);

    if (pid < 0) {
        /* forkpty failed */
        return -1;
    }

    if (pid == 0) {
        /* Child process — become the shell */

        /* Set up a clean environment */
        setenv("TERM", "xterm-256color", 1);
        setenv("COLORTERM", "truecolor", 1);
        setenv("LANG", "en_US.UTF-8", 1);

        /* Preserve HOME and PATH if set, otherwise provide defaults */
        if (getenv("HOME") == NULL) {
            setenv("HOME", "/data/data/com.holger.smartterminal/files", 1);
        }

        if (getenv("PATH") == NULL) {
            setenv("PATH", "/usr/local/bin:/usr/bin:/bin:/sbin:/system/bin", 1);
        }

        /* Execute the shell */
        execlp(shell, shell, "-l", (char *)NULL);

        /* If exec fails, exit */
        _exit(127);
    }

    /* Parent process */
    *out_master_fd = master_fd;
    *out_pid = (int)pid;

    /* Set master fd to non-blocking would complicate read loop; keep blocking */
    return 0;
}

/*
 * pty_read: Read from the master fd (shell output).
 * Returns bytes read, 0 on EOF, -1 on error.
 */
int pty_read(int master_fd, char *buffer, int buffer_size) {
    ssize_t n;
    do {
        n = read(master_fd, buffer, (size_t)buffer_size);
    } while (n < 0 && errno == EINTR);

    if (n < 0) {
        if (errno == EAGAIN)
            return 0;  /* Non-blocking: no data available */
        return -1;     /* Real error */
    }
    return (int)n;     /* 0 = EOF, >0 = bytes read */
}

/*
 * pty_write: Write to the master fd (keyboard input → shell stdin).
 * Returns bytes written, -1 on error.
 */
int pty_write(int master_fd, const char *data, int length) {
    ssize_t total = 0;
    while (total < length) {
        ssize_t n = write(master_fd, data + total, (size_t)(length - total));
        if (n < 0) {
            if (errno == EAGAIN || errno == EINTR)
                continue;
            return -1;
        }
        total += n;
    }
    return (int)total;
}

/*
 * pty_resize: Inform the PTY of a new window size.
 */
int pty_resize(int master_fd, int rows, int cols) {
    struct winsize ws;
    ws.ws_row = (unsigned short)rows;
    ws.ws_col = (unsigned short)cols;
    ws.ws_xpixel = 0;
    ws.ws_ypixel = 0;
    return ioctl(master_fd, TIOCSWINSZ, &ws);
}

/*
 * pty_close: Close the master fd.
 */
void pty_close(int master_fd) {
    close(master_fd);
}

/*
 * pty_kill: Send SIGHUP then SIGKILL to the child process.
 */
void pty_kill(int pid) {
    if (pid <= 0) return;
    kill(pid, SIGHUP);
    usleep(100000); /* 100ms grace period */
    kill(pid, SIGKILL);
}

/*
 * pty_waitpid: Wait for child exit, return exit code.
 */
int pty_waitpid(int pid) {
    int status = 0;
    waitpid(pid, &status, 0);
    if (WIFEXITED(status))
        return WEXITSTATUS(status);
    return -1;
}
