// Each test has a database of its own, so they run side by side.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
