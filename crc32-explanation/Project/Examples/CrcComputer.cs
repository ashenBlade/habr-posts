using System.Runtime.Intrinsics.Arm;

namespace Examples;

/// <summary>
/// Подсчет CRC, использую CRC32
/// </summary>
public static class CrcComputer
{
    /// <summary>
    /// Изначальное значение регистра для табличной реализации
    /// </summary>
    public const uint InitialOptimizedRegister = 0xC704DD7B;
    
    /// <summary>
    /// Изначальное значение регистра для побайтовой реализации
    /// </summary>
    public const uint InitialSimpleRegister = 0xFFFFFFFF;
    
    /// <summary>
    /// Полином
    /// </summary>
    public const uint Polynomial = 0x04C11DB7;
    
    private static readonly uint[] Table = ComputeCrcTable();
    
    private static uint[] ComputeCrcTable()
    {
        var table = new uint[256];
        
        for (int i = 0; i < 256; i++)
        {
            var crc = ( uint )( i << 24 );
            for (int bit = 0; bit < 8; bit++)
            {
                var bitSet = ( crc & 0x80000000 ) != 0;
                crc <<= 1;
                if (bitSet)
                {
                    crc ^= Polynomial;
                }
            }
        
            table[i] = crc;
        }
        
        return table;
    }

    public static byte ComputePerBit(byte[] payload)
    {
        // var register = InitialSimpleRegister;
        var register = (byte) 0;
        var Polynomial = ( byte ) 0b00101101;
        foreach (var bit in IterateBits())
        {
            var bitSet = ( register & 0x80 ) != 0;
            register <<= 1;
            register |= bit;
            if (bitSet)
            {
                register ^= Polynomial;
            }
        }
        
        // Обрабатываем нулевые биты сообщения (дополненные)
        for (var i = 0; i < 8; i++)
        {
            var bitSet = ( register & 0x80 ) != 0;
            register <<= 1;
            // Дальше идут только 0
            // register |= bitSet;
            if (bitSet)
            {
                register ^= Polynomial;
            }
        }
        
        return register;

        IEnumerable<byte> IterateBits()
        {
            foreach (var b in payload)
            {
                for (byte byteMask = 0b10000000; byteMask != 0; byteMask >>= 1)
                {
                    yield return (byte)( ( b & byteMask ) == 0
                                       ? 0
                                       : 1 );
                }
            }
        }
    }

    public static uint ComputeTableSimple(byte[] payload)
    {
        var register = InitialSimpleRegister;

        foreach (var b in payload)
        {
            register = ( ( register << 8 ) | b ) ^ Table[register >> 24];
        }

        for (int i = 0; i < 4; i++)
        {
            register = ( register << 8 ) ^ Table[register >> 24];
        }
        
        return register;
    }

    public static uint ComputeTableOptimized(byte[] payload)
    {
        var register = InitialOptimizedRegister;

        foreach (var b in payload)
        {
            register = ( register << 8 ) ^ Table[( register >> 24 ) ^ b];
        }
        
        return register;
    }

    public static uint ComputeNewTableValue(uint oldInitialValue)
    {
        var register = oldInitialValue;
        
        // Используем побитовое, т.к. вычислить можно в compile time
        // Не делаю оптимизацию, чтобы было лучше понятно как работает
        for (var i = 0; i < 32; i++)
        {
            var bitSet = ( register & 0x80000000 ) != 0;
            register <<= 1;
            if (bitSet)
            {
                register ^= Polynomial;
            }
        }
        
        return register;
    }
}