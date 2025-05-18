// Script para manejar las notificaciones en el navbar
$(document).ready(function () {
    // Referencia a los elementos del DOM
    const $notificationsButton = $('#notificationsButton');
    const $notificationsMenu = $('#notificationsMenu');
    const $notificationBadge = $('.notification-badge');
    
    // Variable para almacenar el recuento anterior de notificaciones
    let previousNotificationCount = 0;
    let firstLoad = true;
    
    // Inicializar
    loadNotifications();
    
    // Manejar clic en el botón de notificaciones
    $notificationsButton.on('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        console.log('Botón de notificaciones clickeado');
        
        // Alternar la clase show en el menú
        $notificationsMenu.toggleClass('show');
        console.log('Estado del menú:', $notificationsMenu.hasClass('show') ? 'visible' : 'oculto');
        
        // Cargar notificaciones solo si está abriendo el menú
        if ($notificationsMenu.hasClass('show')) {
            loadNotifications();
        }
    });
    
    // Cerrar menú al hacer clic fuera de él
    $(document).on('click', function (e) {
        if (!$notificationsButton.is(e.target) && 
            !$notificationsButton.has(e.target).length &&
            !$notificationsMenu.is(e.target) && 
            $notificationsMenu.has(e.target).length === 0) {
            
            if ($notificationsMenu.hasClass('show')) {
                console.log('Cerrando menú de notificaciones (clic externo)');
                $notificationsMenu.removeClass('show');
            }
        }
    });
    
    // Función para cargar las notificaciones
    function loadNotifications() {
        console.log('Cargando notificaciones...');
        $notificationsMenu.html('<div class="p-3 text-center"><span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Cargando notificaciones...</div>');
        
        $.ajax({
            url: '/Alert/GetNavbarNotifications',
            type: 'GET',
            success: function (data) {
                console.log('Notificaciones cargadas correctamente');
                // Actualizar el menú de notificaciones
                $notificationsMenu.html(data);
                
                // Contar notificaciones no resueltas y actualizar el badge
                const notificationCount = $('#notificationsMenu .notification-item').length;
                console.log('Número de notificaciones: ' + notificationCount);
                
                if (notificationCount > 0) {
                    $notificationBadge.text(notificationCount).show();
                } else {
                    $notificationBadge.hide();
                }
                
                // Asegurarse de que el botón "Ver todas las alertas" tenga el ancho correcto
                $('.notification-footer .btn').addClass('w-100');
                
                // En la primera carga, establecer el contador inicial
                if (firstLoad) {
                    previousNotificationCount = notificationCount;
                    firstLoad = false;
                }
            },
            error: function (xhr, status, error) {
                console.error('Error al cargar notificaciones:', error);
                console.error('Status:', status);
                console.error('Response:', xhr.responseText);
                $notificationsMenu.html('<div class="p-3 text-center text-danger">Error al cargar las notificaciones</div>');
                $notificationBadge.hide();
            }
        });
    }
    
    // Función para actualizar la tabla de notificaciones en la página de alertas
    function updateNotificationsTable() {
        // Solo actualizar si estamos en la página de alertas (URL contiene /Alert/Index)
        if (window.location.pathname.indexOf('/Alert/Index') !== -1) {
            console.log('Actualizando tabla de notificaciones...');
            $.ajax({
                url: '/Alert/GetNotificationsTable',
                type: 'GET',
                success: function (data) {
                    // Reemplazar la tabla de notificaciones con los nuevos datos
                    $('#notificationsTableContainer').html(data);
                    console.log('Tabla de notificaciones actualizada correctamente');
                    
                    // Reinicializar los event listeners para los textos truncados
                    initTruncatedTextHandlers();
                },
                error: function (xhr, status, error) {
                    console.error('Error al actualizar tabla de notificaciones:', error);
                }
            });
        }
    }
    
    // Función para inicializar los manejadores de eventos para los textos truncados
    function initTruncatedTextHandlers() {
        if (window.location.pathname.indexOf('/Alert/Index') !== -1) {
            const truncatedElements = document.querySelectorAll('.truncate-text');
            
            // Comprobar si el modal de Bootstrap está disponible
            let textModal;
            try {
                textModal = new bootstrap.Modal(document.getElementById('textModal'));
            } catch (e) {
                console.error('Error al inicializar el modal de Bootstrap:', e);
                return;
            }
            
            const modalContent = document.getElementById('textModalContent');
            
            truncatedElements.forEach(element => {
                element.addEventListener('click', function() {
                    const fullText = this.getAttribute('data-full-text');
                    if (modalContent) {
                        modalContent.textContent = fullText;
                        textModal.show();
                    }
                });
            });
        }
    }
    
    // Función para reproducir un sonido de notificación
    function playNotificationSound() {
        try {
            // Crear un contexto de audio y un oscilador para generar un tono simple
            const audioContext = new (window.AudioContext || window.webkitAudioContext)();
            const oscillator = audioContext.createOscillator();
            const gainNode = audioContext.createGain();
            
            oscillator.type = 'sine';
            oscillator.frequency.setValueAtTime(880, audioContext.currentTime); // La nota A5
            gainNode.gain.setValueAtTime(0.5, audioContext.currentTime);
            
            oscillator.connect(gainNode);
            gainNode.connect(audioContext.destination);
            
            oscillator.start();
            // Detener después de 0.2 segundos
            oscillator.stop(audioContext.currentTime + 0.2);
            
            // Reproducir un segundo tono después de una breve pausa
            setTimeout(() => {
                const oscillator2 = audioContext.createOscillator();
                oscillator2.type = 'sine';
                oscillator2.frequency.setValueAtTime(1318.51, audioContext.currentTime); // La nota E6
                oscillator2.connect(gainNode);
                
                oscillator2.start();
                oscillator2.stop(audioContext.currentTime + 0.15);
            }, 250);
            
        } catch (e) {
            console.log('Error al reproducir sonido de notificación:', e);
        }
    }
    
    // Actualizar notificaciones cada 5 segundos
    setInterval(function() {
        $.ajax({
            url: '/Alert/GetNavbarNotifications',
            type: 'GET',
            success: function (data) {
                // Contar notificaciones y actualizar el badge
                const tempDiv = $('<div>').html(data);
                const notificationCount = tempDiv.find('.notification-item').length;
                
                // Verificar si hay nuevas notificaciones (solo después de la primera carga)
                if (!firstLoad && notificationCount > previousNotificationCount) {
                    console.log('¡Nuevas notificaciones detectadas!');
                    
                    // Reproducir sonido de notificación
                    playNotificationSound();
                    
                    // Si el menú no está visible, mostrarlo
                    if (!$notificationsMenu.hasClass('show')) {
                        $notificationsMenu.html(data);
                        $notificationsMenu.addClass('show');
                    } else {
                        // Si ya está visible, solo actualizar el contenido
                        $notificationsMenu.html(data);
                    }
                    
                    // Actualizar la tabla de notificaciones en la página de alertas
                    updateNotificationsTable();
                } else if (notificationCount !== previousNotificationCount) {
                    // Si cambió el número de notificaciones pero no aumentó
                    if ($notificationsMenu.hasClass('show')) {
                        // Si el menú está visible, actualizar su contenido
                        $notificationsMenu.html(data);
                    }
                    
                    // Actualizar la tabla si estamos en la página de alertas
                    updateNotificationsTable();
                }
                
                // Actualizar el badge siempre, independientemente de si hay nuevas notificaciones
                if (notificationCount > 0) {
                    $notificationBadge.text(notificationCount).show();
                } else {
                    $notificationBadge.hide();
                }
                
                // Guardar el recuento actual para la próxima comparación
                previousNotificationCount = notificationCount;
            }
        });
    }, 5000); // 5 segundos
}); 