using System;

public class SnowflakeIdGenerator
{
    // Epoch (2020-01-01T00:00:00Z)
    private static readonly DateTime Epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly object _lock = new object();
    private readonly long _machineId;
    private readonly long _machineIdBits = 10L; // Up to 1024 machines
    private readonly long _sequenceBits = 12L;  // Up to 4096 IDs per ms per machine

    private readonly long _maxMachineId;
    private readonly long _maxSequence;

    private long _lastTimestamp = -1L;
    private long _sequence = 0L;

    public SnowflakeIdGenerator(long machineId)
    {
        _maxMachineId = -1L ^ (-1L << (int)_machineIdBits);
        _maxSequence = -1L ^ (-1L << (int)_sequenceBits);

        if (machineId < 0 || machineId > _maxMachineId)
            throw new ArgumentException($"MachineId must be between 0 and {_maxMachineId}");

        _machineId = machineId;
    }

    public long NextId()
    {
        lock (_lock)
        {
            long timestamp = GetCurrentTimestamp();

            if (timestamp < _lastTimestamp)
            {
                throw new InvalidOperationException("Clock moved backwards. Refusing to generate id.");
            }

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & _maxSequence;
                if (_sequence == 0)
                {
                    // Sequence exhausted, wait till next ms
                    timestamp = WaitNextMillis(_lastTimestamp);
                }
            }
            else
            {
                _sequence = 0L;
            }

            _lastTimestamp = timestamp;

            long id = ((timestamp << ((int)_machineIdBits + (int)_sequenceBits))
                        | (_machineId << (int)_sequenceBits)
                        | _sequence);

            return id;
        }
    }

    private long GetCurrentTimestamp()
    {
        return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
    }

    private long WaitNextMillis(long lastTimestamp)
    {
        long timestamp = GetCurrentTimestamp();
        while (timestamp <= lastTimestamp)
        {
            timestamp = GetCurrentTimestamp();
        }
        return timestamp;
    }
}