const fs = require('fs');
var path = 'ServerSide/ReactServerSide/DAL';
var files = fs.readdirSync(path).filter(function(x){return x.endsWith('.js');});
files.forEach(function(fn){
  var c = fs.readFileSync(path + '/' + fn, 'utf8');
  var m = c.match(/Password\s*[:=]\s*['"][^'"]+['"]/gi);
  if (m) console.log(fn + ' (pw): ' + m.slice(0,5).join(' | '));
  var m2 = c.match(/christian[^\n]*/gi);
  if (m2) m2.slice(0,3).forEach(function(x){ console.log(fn + ' (christian): ' + x.trim()); });
  var m3 = c.match(/defaultPassword[^\n]*/gi);
  if (m3) m3.slice(0,5).forEach(function(x){ console.log(fn + ' (default): ' + x.trim()); });
});
