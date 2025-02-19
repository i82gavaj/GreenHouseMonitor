document.addEventListener('DOMContentLoaded', function() {
    // Actualizar reloj
    function updateClock() {
        const now = new Date();
        const timeString = now.toLocaleTimeString();
        document.getElementById('currentTime').textContent = timeString;
    }
    
    setInterval(updateClock, 1000);
    updateClock();

    // Configurar gráficas
    const temperatureCtx = document.getElementById('temperatureChart').getContext('2d');
    const humidityCtx = document.getElementById('humidityChart').getContext('2d');

    // Gráfica de temperatura
    new Chart(temperatureCtx, {
        type: 'line',
        data: {
            labels: ['12:00', '12:05', '12:10', '12:15', '12:20'],
            datasets: [{
                label: 'Temperatura °C',
                data: [22, 23, 22.5, 23.2, 22.8],
                borderColor: '#dc3545',
                tension: 0.4
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false
        }
    });

    // Gráfica de humedad
    new Chart(humidityCtx, {
        type: 'line',
        data: {
            labels: ['12:00', '12:05', '12:10', '12:15', '12:20'],
            datasets: [{
                label: 'Humedad %',
                data: [45, 46, 44, 47, 45],
                borderColor: '#0dcaf0',
                tension: 0.4
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false
        }
    });
});
