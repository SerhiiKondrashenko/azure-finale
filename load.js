const http = require('k6/http');
const options = {
    vus: 1000,
    duration: '15m'
};

exports.options = options;


exports.default = function() {
    http.get('https://public-api-us-west-2.azurewebsites.net/api/catalog-items/');
}