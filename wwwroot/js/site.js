// site.js - Funzioni JavaScript globali per TravelGpt

// Attendi che il DOM sia completamente caricato
document.addEventListener('DOMContentLoaded', function () {

    // Inizializza tutti i tooltip di Bootstrap
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'))
    var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl)
    });

    // Gestione della navigazione smooth scroll per ancore
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            e.preventDefault();

            const targetId = this.getAttribute('href');
            if (targetId === '#') return;

            const targetElement = document.querySelector(targetId);
            if (targetElement) {
                window.scrollTo({
                    top: targetElement.offsetTop - 70, // Offset per la navbar
                    behavior: 'smooth'
                });
            }
        });
    });

    // Gestione del back-to-top button (se presente)
    const backToTopButton = document.getElementById('back-to-top');
    if (backToTopButton) {
        window.addEventListener('scroll', () => {
            if (window.pageYOffset > 300) {
                backToTopButton.classList.add('show');
            } else {
                backToTopButton.classList.remove('show');
            }
        });

        backToTopButton.addEventListener('click', () => {
            window.scrollTo({
                top: 0,
                behavior: 'smooth'
            });
        });
    }

    // Gestione della navbar transparent-to-solid su scroll (se presente)
    const navbar = document.querySelector('.navbar-scroll');
    if (navbar) {
        window.addEventListener('scroll', () => {
            if (window.scrollY > 50) {
                navbar.classList.add('navbar-scrolled');
            } else {
                navbar.classList.remove('navbar-scrolled');
            }
        });
    }

    // Funzione helper per formattare valute
    window.formatCurrency = function (amount, currency = '€') {
        return `${currency}${parseFloat(amount).toFixed(2)}`;
    };

    // Funzione helper per formattare date
    window.formatDate = function (dateString) {
        const date = new Date(dateString);
        return date.toLocaleDateString('it-IT', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric'
        });
    };

    // Funzione helper per truncare testo
    window.truncateText = function (text, maxLength) {
        if (!text) return '';
        return text.length > maxLength ? text.substring(0, maxLength) + '...' : text;
    };

    // Validazione lato client personalizzata (esempio)
    const formElements = document.querySelectorAll('.needs-validation');
    Array.prototype.slice.call(formElements).forEach(form => {
        form.addEventListener('submit', event => {
            if (!form.checkValidity()) {
                event.preventDefault();
                event.stopPropagation();
            }
            form.classList.add('was-validated');
        }, false);
    });
});