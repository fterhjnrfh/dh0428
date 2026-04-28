cd C:\\Users\\Administrator 
netsh wlan show drivers
netsh wlan set hostednetwork mode = allow 
netsh wlan set hostednetwork ssid = DH5928W
netsh wlan set hostednetwork key = 11111111
netsh wlan stop hostednetwork
netsh wlan start hostednetwork

