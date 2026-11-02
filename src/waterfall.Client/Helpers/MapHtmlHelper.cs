// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Globalization;

namespace Waterfall.Client.Helpers;

/// <summary>
/// Генератор HTML-сторінки з інтерактивною візуальною картою (Leaflet / OpenStreetMap)
/// для відображення вибраної точки та адреси користувача.
/// </summary>
public static class MapHtmlHelper
{
    /// <summary>
    /// Генерує самодостатній HTML-код для WebView із картою OpenStreetMap та маркером локації.
    /// </summary>
    /// <param name="latitude">Географічна широта.</param>
    /// <param name="longitude">Географічна довгота.</param>
    /// <param name="address">Текстова адреса або назва вибраної точки.</param>
    /// <returns>HTML-рядок для завантаження у WebView.</returns>
    public static string GenerateMapHtml(double latitude, double longitude, string address)
    {
        string latStr = latitude.ToString("F6", CultureInfo.InvariantCulture);
        string lonStr = longitude.ToString("F6", CultureInfo.InvariantCulture);
        string safeAddress = (address ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("'", "\\'")
            .Replace("\r", "")
            .Replace("\n", " ");

        double minLat = latitude - 0.006;
        double maxLat = latitude + 0.006;
        double minLon = longitude - 0.010;
        double maxLon = longitude + 0.010;

        string minLatStr = minLat.ToString("F6", CultureInfo.InvariantCulture);
        string maxLatStr = maxLat.ToString("F6", CultureInfo.InvariantCulture);
        string minLonStr = minLon.ToString("F6", CultureInfo.InvariantCulture);
        string maxLonStr = maxLon.ToString("F6", CultureInfo.InvariantCulture);

        return $$"""
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
            <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
            <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js" onerror="loadFallback()"></script>
            <style>
                html, body {
                    height: 100%;
                    margin: 0;
                    padding: 0;
                    background-color: #12121e;
                    overflow: hidden;
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                }
                #map {
                    width: 100%;
                    height: 100%;
                    background: #12121e;
                }
                .custom-pin {
                    width: 24px;
                    height: 24px;
                    border-radius: 50% 50% 50% 0;
                    background: #6366f1;
                    position: absolute;
                    transform: rotate(-45deg);
                    left: 50%;
                    top: 50%;
                    margin: -20px 0 0 -12px;
                    box-shadow: 0 0 12px rgba(99, 102, 241, 0.9);
                    border: 2px solid #ffffff;
                }
                .custom-pin::after {
                    content: '';
                    width: 10px;
                    height: 10px;
                    margin: 5px 0 0 5px;
                    background: #ffffff;
                    position: absolute;
                    border-radius: 50%;
                }
                .custom-pulse {
                    background: rgba(99, 102, 241, 0.4);
                    border-radius: 50%;
                    height: 14px;
                    width: 14px;
                    position: absolute;
                    left: 50%;
                    top: 50%;
                    margin: 5px 0 0 -7px;
                    transform: rotateX(55deg);
                    z-index: -2;
                    animation: pulsate 1.5s ease-out infinite;
                }
                @keyframes pulsate {
                    0% { transform: scale(0.1, 0.1); opacity: 0.0; }
                    50% { opacity: 1.0; }
                    100% { transform: scale(2.0, 2.0); opacity: 0.0; }
                }
                .leaflet-popup-content-wrapper {
                    background: #1e1e2f;
                    color: #ffffff;
                    border-radius: 8px;
                    border: 1px solid #3b3b54;
                    box-shadow: 0 4px 16px rgba(0,0,0,0.5);
                    font-size: 12px;
                }
                .leaflet-popup-tip {
                    background: #1e1e2f;
                }
            </style>
        </head>
        <body>
            <div id="map"></div>
            <script>
                function loadFallback() {
                    document.getElementById('map').innerHTML = 
                        '<div style="display:flex;flex-direction:column;align-items:center;justify-content:center;height:100%;color:#ffffff;background:#1a1a2e;text-align:center;padding:16px;">' +
                        '<div style="font-size:32px;margin-bottom:8px;">📍</div>' +
                        '<div style="font-size:14px;font-weight:bold;margin-bottom:4px;">{{safeAddress}}</div>' +
                        '<div style="font-size:12px;color:#a0a0b8;margin-bottom:12px;">{{latStr}}, {{lonStr}}</div>' +
                        '<a href="https://www.openstreetmap.org/?mlat={{latStr}}&amp;mlon={{lonStr}}#map=16/{{latStr}}/{{lonStr}}" target="_blank" style="color:#818cf8;font-size:12px;text-decoration:none;border:1px solid #6366f1;padding:6px 12px;border-radius:6px;background:rgba(99,102,241,0.15);">Відкрити на карті ↗</a>' +
                        '</div>';
                }

                try {
                    var lat = {{latStr}};
                    var lon = {{lonStr}};
                    if (typeof L !== 'undefined') {
                        var map = L.map('map', { zoomControl: true, attributionControl: false }).setView([lat, lon], 16);
                        
                        // Використовуємо OpenStreetMap France як основний шар тайлів
                        var tileLayer = L.tileLayer('https://{s}.tile.openstreetmap.fr/osmfr/{z}/{x}/{y}.png', {
                            maxZoom: 20,
                            attribution: '&copy; OpenStreetMap France &copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
                        });

                        tileLayer.on('tileerror', function() {
                            // Fallback на ESRI World Street Map у разі збою мережі
                            L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}', {
                                maxZoom: 19,
                                attribution: '&copy; Esri &copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
                            }).addTo(map);
                        });

                        tileLayer.addTo(map);

                        var pinIcon = L.divIcon({
                            className: 'custom-pin-container',
                            html: '<div class="custom-pulse"></div><div class="custom-pin"></div>',
                            iconSize: [30, 42],
                            iconAnchor: [15, 38],
                            popupAnchor: [0, -36]
                        });

                        var marker = L.marker([lat, lon], { icon: pinIcon }).addTo(map);
                        marker.bindPopup("<b>📍 {{safeAddress}}</b><br/><span style='color:#a0a0b8'>" + lat + ", " + lon + "</span>").openPopup();
                    } else {
                        loadFallback();
                    }
                } catch(e) {
                    loadFallback();
                }
            </script>
        </body>
        </html>
        """;
    }
}
