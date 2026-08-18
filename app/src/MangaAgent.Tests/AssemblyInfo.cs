using Xunit;

// Avalonia Headless 需要串行执行（多测试类并发操作同一 headless 平台会死锁）
[assembly: CollectionBehavior(DisableTestParallelization = true)]
