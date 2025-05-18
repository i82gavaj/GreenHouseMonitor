-- Script para analizar los valores de los sensores
-- Proporciona información sobre rangos, patrones y estadísticas

-- 1. Estadísticas de valores por tipo de sensor
SELECT 
    s.SensorType,
    CASE 
        WHEN s.SensorType = 0 THEN 'Temperatura'
        WHEN s.SensorType = 1 THEN 'Humedad'
        WHEN s.SensorType = 2 THEN 'CO2'
        WHEN s.SensorType = 3 THEN 'Luminosidad'
        ELSE 'Desconocido'
    END AS TipoSensor,
    COUNT(*) AS NumeroRegistros,
    MIN(a.CurrentValue) AS ValorMinimo,
    MAX(a.CurrentValue) AS ValorMaximo,
    AVG(a.CurrentValue) AS ValorPromedio,
    STDEV(a.CurrentValue) AS DesviacionEstandar
FROM Alerts a
JOIN Sensors s ON a.SensorID = s.SensorID
GROUP BY s.SensorType
ORDER BY s.SensorType;

-- 2. Distribución de valores para sensores de temperatura
SELECT 
    CASE 
        WHEN a.CurrentValue BETWEEN 0 AND 10 THEN '0-10'
        WHEN a.CurrentValue BETWEEN 10 AND 20 THEN '10-20'
        WHEN a.CurrentValue BETWEEN 20 AND 30 THEN '20-30'
        WHEN a.CurrentValue BETWEEN 30 AND 40 THEN '30-40'
        WHEN a.CurrentValue BETWEEN 40 AND 50 THEN '40-50'
        WHEN a.CurrentValue BETWEEN 50 AND 100 THEN '50-100'
        WHEN a.CurrentValue BETWEEN 100 AND 1000 THEN '100-1000'
        WHEN a.CurrentValue BETWEEN 1000 AND 5000 THEN '1000-5000'
        WHEN a.CurrentValue > 5000 THEN '>5000'
        ELSE '<0'
    END AS RangoValor,
    COUNT(*) AS Cantidad
FROM Alerts a
JOIN Sensors s ON a.SensorID = s.SensorID
WHERE s.SensorType = 0  -- Temperatura
GROUP BY 
    CASE 
        WHEN a.CurrentValue BETWEEN 0 AND 10 THEN '0-10'
        WHEN a.CurrentValue BETWEEN 10 AND 20 THEN '10-20'
        WHEN a.CurrentValue BETWEEN 20 AND 30 THEN '20-30'
        WHEN a.CurrentValue BETWEEN 30 AND 40 THEN '30-40'
        WHEN a.CurrentValue BETWEEN 40 AND 50 THEN '40-50'
        WHEN a.CurrentValue BETWEEN 50 AND 100 THEN '50-100'
        WHEN a.CurrentValue BETWEEN 100 AND 1000 THEN '100-1000'
        WHEN a.CurrentValue BETWEEN 1000 AND 5000 THEN '1000-5000'
        WHEN a.CurrentValue > 5000 THEN '>5000'
        ELSE '<0'
    END
ORDER BY MIN(a.CurrentValue);

-- 3. Análisis de dígitos para sensores de temperatura
SELECT 
    LEN(CAST(CAST(a.CurrentValue AS INT) AS VARCHAR)) AS NumeroDígitos,
    COUNT(*) AS Cantidad,
    MIN(a.CurrentValue) AS ValorMinimo,
    MAX(a.CurrentValue) AS ValorMaximo,
    AVG(a.CurrentValue) AS ValorPromedio
FROM Alerts a
JOIN Sensors s ON a.SensorID = s.SensorID
WHERE s.SensorType = 0  -- Temperatura
GROUP BY LEN(CAST(CAST(a.CurrentValue AS INT) AS VARCHAR))
ORDER BY NumeroDígitos;

-- 4. Identificar posibles factores de conversión para temperatura
SELECT 
    s.SensorID,
    s.SensorName,
    COUNT(*) AS NumeroMediciones,
    MIN(a.CurrentValue) AS ValorMinimo,
    MAX(a.CurrentValue) AS ValorMaximo,
    AVG(a.CurrentValue) AS ValorPromedio,
    MIN(a.CurrentValue) / 10 AS PosibleValorReal_Div10,
    MIN(a.CurrentValue) / 100 AS PosibleValorReal_Div100,
    MIN(a.CurrentValue) / 1000 AS PosibleValorReal_Div1000
FROM Alerts a
JOIN Sensors s ON a.SensorID = s.SensorID
WHERE s.SensorType = 0  -- Temperatura
  AND a.CurrentValue > 100  -- Valores potencialmente anómalos
GROUP BY s.SensorID, s.SensorName
ORDER BY AVG(a.CurrentValue) DESC;

-- 5. Ver valores recientes para verificar el formato
SELECT TOP 20
    a.AlertID,
    s.SensorName,
    CASE 
        WHEN s.SensorType = 0 THEN 'Temperatura'
        WHEN s.SensorType = 1 THEN 'Humedad'
        WHEN s.SensorType = 2 THEN 'CO2'
        WHEN s.SensorType = 3 THEN 'Luminosidad'
        ELSE 'Desconocido'
    END AS TipoSensor,
    a.CurrentValue AS ValorOriginal,
    CASE 
        WHEN s.SensorType = 0 THEN  -- Temperatura
            CASE 
                WHEN a.CurrentValue BETWEEN -40 AND 125 THEN a.CurrentValue  -- Dentro de rango
                WHEN a.CurrentValue > 125 AND a.CurrentValue < 1000 THEN a.CurrentValue / 10
                WHEN a.CurrentValue >= 1000 AND a.CurrentValue < 10000 THEN a.CurrentValue / 100
                WHEN a.CurrentValue >= 10000 THEN a.CurrentValue / 1000
                ELSE a.CurrentValue
            END
        ELSE a.CurrentValue
    END AS ValorNormalizado,
    a.CreatedAt
FROM Alerts a
JOIN Sensors s ON a.SensorID = s.SensorID
ORDER BY a.CreatedAt DESC; 