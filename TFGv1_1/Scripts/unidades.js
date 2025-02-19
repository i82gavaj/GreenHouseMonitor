//unidades.js
$(document).ready(function () {
    $('#sensorType').change(function () {
        var selectedType = $(this).val();
        var unitsDropdown = $('#units');
        unitsDropdown.empty();

        if (selectedType === 'Temperature') {
            unitsDropdown.append($('<option></option>').val('GCelsius').html('GCelsius'));
        } else if (selectedType === 'CO2') {
            unitsDropdown.append($('<option></option>').val('microgm3').html('microgm3'));
        } else if (selectedType === 'Brightness') {
            unitsDropdown.append($('<option></option>').val('Lumen').html('Lumen'));
        } else if (selectedType === 'Humidity') {
            unitsDropdown.append($('<option></option>').val('gm3').html('gm3'));
        }
    });
});