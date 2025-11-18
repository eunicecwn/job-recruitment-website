let map;
let marker;

window.addEventListener("load", function () {
    map = L.map('map').setView([4.2105, 101.9758], 7); // Malaysia center

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; OpenStreetMap contributors'
    }).addTo(map);

    const latInput = document.querySelector('input[name="Latitude"]');
    const lngInput = document.querySelector('input[name="Longitude"]');
    const locationInput = document.getElementById("location-input");
    const suggestions = document.getElementById("suggestions");

    function setMarker(lat, lng) {
        if (marker) {
            marker.setLatLng([lat, lng]);
        } else {
            marker = L.marker([lat, lng], { draggable: true }).addTo(map);
            marker.on('dragend', function () {
                const pos = marker.getLatLng();
                updateLatLng(pos.lat, pos.lng);
                reverseGeocode(pos.lat, pos.lng);
            });
        }
        updateLatLng(lat, lng);
    }

    function updateLatLng(lat, lng) {
        latInput.value = lat.toFixed(6);
        lngInput.value = lng.toFixed(6);
    }

    function reverseGeocode(lat, lng) {
        fetch(`https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=${lat}&lon=${lng}`)
            .then(response => response.json())
            .then(data => {
                if (data && data.display_name) {
                    locationInput.value = data.display_name;
                }
            });
    }

    function geocode(query) {
        return fetch(`https://nominatim.openstreetmap.org/search?format=jsonv2&q=${encodeURIComponent(query)}`)
            .then(res => res.json());
    }

    map.on("click", function (e) {
        setMarker(e.latlng.lat, e.latlng.lng);
        reverseGeocode(e.latlng.lat, e.latlng.lng);
    });

    if (latInput.value && lngInput.value) {
        const lat = parseFloat(latInput.value);
        const lng = parseFloat(lngInput.value);
        setMarker(lat, lng);
        map.setView([lat, lng], 15);
    }

    locationInput.addEventListener("input", function () {
        const value = this.value.trim();
        if (value.length < 3) {
            suggestions.style.display = "none";
            return;
        }

        geocode(value).then(results => {
            suggestions.innerHTML = '';
            if (results.length === 0) {
                suggestions.style.display = "none";
                return;
            }

            results.slice(0, 5).forEach(place => {
                const div = document.createElement("div");
                div.textContent = place.display_name;
                div.addEventListener("click", () => {
                    locationInput.value = place.display_name;
                    setMarker(parseFloat(place.lat), parseFloat(place.lon));
                    map.setView([parseFloat(place.lat), parseFloat(place.lon)], 15);
                    suggestions.style.display = "none";
                });
                suggestions.appendChild(div);
            });
            suggestions.style.display = "block";
        });
    });

    document.addEventListener("click", function (e) {
        if (e.target !== locationInput) {
            suggestions.style.display = "none";
        }
    });

    setTimeout(() => {
        map.invalidateSize();
    }, 100);
});
