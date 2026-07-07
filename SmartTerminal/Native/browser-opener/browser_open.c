/*
 * browser_open.c -> libbrowseropener.so
 *
 * Claude Code's browser-open (cli.js J3) runs `$BROWSER <url>`, else xdg-open —
 * which doesn't exist on Android, so OAuth login prints "Browser didn't open?".
 * We set BROWSER=<this> (in nativeLibraryDir, the only exec-allowed dir), so claude
 * hands us the URL. Ships as lib*.so (SELinux exec constraint).
 *
 * v1 exec'd /system/bin/am — DEAD on real devices: am's binder call to the activity
 * service is denied for untrusted_app (Android 12: "Failure calling service activity:
 * Failed transaction 2147483646"; ROADMAP 2026-07-07). Same reason Termux ships its
 * own am. v2 SIGNALS THE APP instead: write the URL to $TMPDIR/open-url (atomic
 * tmp+rename so the watcher never reads a partial write); NodeRuntimeService's
 * FileObserver picks it up and calls Browser.OpenAsync from the app process, which
 * IS allowed to start activities. am stays as fallback for shell-domain runs (adb),
 * where it works and no watcher may be listening.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <fcntl.h>

int main(int argc, char **argv) {
    if (argc < 2) return 1;
    const char *tmp = getenv("TMPDIR");
    if (tmp && *tmp) {
        char tmppath[4096], path[4096];
        snprintf(tmppath, sizeof tmppath, "%s/open-url.tmp", tmp);
        snprintf(path, sizeof path, "%s/open-url", tmp);
        int fd = open(tmppath, O_WRONLY | O_CREAT | O_TRUNC, 0600);
        if (fd >= 0) {
            size_t len = strlen(argv[1]);
            ssize_t w = write(fd, argv[1], len);
            close(fd);
            if (w == (ssize_t)len && rename(tmppath, path) == 0)
                return 0;
            unlink(tmppath);
        }
    }
    /* Fallback: works from the shell domain (adb), not from untrusted_app. */
    execl("/system/bin/am", "am", "start",
          "-a", "android.intent.action.VIEW",
          "-d", argv[1], (char *)0);
    return 127; /* exec failed */
}
