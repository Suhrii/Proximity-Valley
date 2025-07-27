import Ice
import Murmur
import asyncio
import websockets
import json

ICE_HOST = "127.0.0.1"
ICE_PORT = 6502
MUMBLE_CHANNELS = {
    "Farm": "FarmChannel",
    "Town": "TownChannel",
    "Mine": "MineChannel"
}

class MumbleController:
    def __init__(self):
        self.ic = Ice.initialize()
        proxy = self.ic.stringToProxy(f"Meta:tcp -h {ICE_HOST} -p {ICE_PORT}")
        self.meta = Murmur.MetaPrx.checkedCast(proxy)
        self.server = self.meta.getAllServers()[0]  # Use first Mumble server

    def move_user(self, username, target_channel_name):
        users = self.server.getUsers()
        channels = self.server.getChannels()
        target_channel = next((c for c in channels.values() if c.name == target_channel_name), None)

        if not target_channel:
            print(f"Channel not found: {target_channel_name}")
            return

        for session, user in users.items():
            if user.name == username:
                self.server.setState(Murmur.User(state=user.session, channel=target_channel.id))
                print(f"Moved {username} to {target_channel.name}")
                break

mumble = MumbleController()

async def handle_client(websocket):
    async for message in websocket:
        data = json.loads(message)
        username = data.get("username")
        location = data.get("location")
        channel = MUMBLE_CHANNELS.get(location)
        if channel:
            mumble.move_user(username, channel)

start_server = websockets.serve(handle_client, "0.0.0.0", 8765)

asyncio.get_event_loop().run_until_complete(start_server)
asyncio.get_event_loop().run_forever()
