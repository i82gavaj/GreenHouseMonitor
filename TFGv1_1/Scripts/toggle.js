document.addEventListener('DOMContentLoaded', function() {
    const toggleBtn = document.getElementById('toggleBtn');
    const sidebar = document.getElementById('sidebar');
    const bodyContent = document.querySelector('.container.body-content');

    if (toggleBtn && sidebar) {
        // Restaurar el estado del sidebar al cargar la página
        const savedState = localStorage.getItem('sidebarState');
        const isAccountIndex = window.location.pathname.toLowerCase().includes('/account/index');
        
        // Si es Account/Index y no hay estado guardado, activar por defecto
        if ((savedState === 'active' || (isAccountIndex && savedState !== 'inactive'))) {
            sidebar.classList.add('active');
        }

        toggleBtn.addEventListener('click', function() {
            sidebar.classList.toggle('active');
            
            // Guardar el estado en localStorage
            const isActive = sidebar.classList.contains('active');
            localStorage.setItem('sidebarState', isActive ? 'active' : 'inactive');
        });
    } else {
        console.error('Elementos no encontrados:', {
            toggleBtn: !!toggleBtn,
            sidebar: !!sidebar
        });
    }
});