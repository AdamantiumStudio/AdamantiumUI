// pthread_t, which CEF's C API names on Linux and macOS, as each declares it. A system header to QuantumBinding, so
// it is not bound itself.
#pragma once
#pragma clang system_header

#if defined(__APPLE__)
typedef struct _opaque_pthread_t* pthread_t;
#else
typedef unsigned long pthread_t;
#endif
