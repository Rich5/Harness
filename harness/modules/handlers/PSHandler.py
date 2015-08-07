'''

Harness Toolset

Copyright (c) 2015 Rich Kelley

Contact: 
    @RGKelley5
    RK5DEVMAIL[A T]gmail[D O T]com
    www.frogstarworldc.com

License: MIT


'''

import sys
import asyncio
import functools
import traceback
from time import sleep
from harness.core import module



class Module(module.ModuleFrame):

    about = {
                'name': 'PSHandler',
                'info': 'Experimental asyncio handler for harness payloads\nAll sessions are managed in the same thread',
                'author': 'Rich',
                'contact': '@RGKelley5',
                'version': '0.1'
        }

    def __init__(self):

        module.ModuleFrame.__init__(self, self.about)

        self.FORCE_THREAD = True
        self.add_option('IP', "0.0.0.0", "str")
        self.add_option('PORT', "80", "int")
        self.tasks = []
        
        
    @asyncio.coroutine
    def client_connected_handler(self, client_reader, client_writer):

        remote_conn_info = client_writer.get_extra_info("peername")
        local_conn_info = client_writer.get_extra_info("sockname")

        SID = self.add_session(remote_conn_info, local_conn_info, stype="asyncio_session")

        # Start a new asyncio.Task to handle this specific client connection
        task = asyncio.async(self.handle_client(SID, client_reader, client_writer))
        
        def client_done(SID, _):
        
            # When the tasks that handles the specific client connection is done
            client_writer.close()
            self.remove_session(SID)
     
        # Add the client_done callback to be run when the future becomes done
        task.add_done_callback(functools.partial(client_done, SID))

     
    @asyncio.coroutine
    def handle_client(self, SID, client_reader, client_writer):
        

        # Handle the requests for a specific client with a line oriented protocol
        while self.isrunning():
            
            cmd = yield from self.get_input(SID)

            if cmd:

                # Begin Remote Module Loading Code
                if cmd != "\r\n" and cmd != "\n":
                    cmd_parts = cmd.split()

                    if cmd_parts[0] == "^import-module":

                        client_writer.write("<rf>".encode())

                        try:
                            with open(cmd_parts[1], 'rb') as f:
                                self.print_output("Sending " + cmd_parts[1])
                                client_writer.writelines(f)

                                yield from client_writer.drain()
                        except OSError:
                            self.print_error("File not found")
                            pass

                        yield from asyncio.sleep(1)             # Give buffer  chance to flush before sending closing tag         
                        client_writer.write("</rf>".encode())   # signal that we're done transfering the module

                    else:
                        client_writer.write(cmd.encode())

                # End Remote Module Loading code. For a plain TCP handler remove this block
                # and just include the following code after the else
                else:
                    
                    client_writer.write(cmd.encode())

                    if cmd.lower() == "exit":
                        break
                    
            while self.isrunning():
                try:
                    if client_reader.at_eof():
                        raise ConnectionError

                    _data = (yield from asyncio.wait_for(client_reader.read(1024),timeout=0.1))
                    self.print(_data.decode(),end="",flush=True)

                except ConnectionError:
                    #traceback.print_exc(file=sys.stdout)
                    return
                    
                except:
                    
                    # Did not received any new data break out
                    break



    @asyncio.coroutine
    def get_input(self, SID):

        yield from self.empty_stdin()
        try:
            
            if not SID in self.sessions:
                return "exit"

            q = self.sessions[SID].session_in    
            _cmd = (yield from asyncio.wait_for(q.get(), timeout=0.1))

            return _cmd
        except:
            
            #traceback.print_exc(file=sys.stdout)
            pass

    @asyncio.coroutine
    def empty_stdin(self):

        loop = asyncio.get_event_loop()

        if len(self.sessions) > 0: 
            while not self.stdin_q.empty():
                    SID, cmd = yield from loop.run_in_executor(None, self.stdin_q.get)
                        
                    if SID in self.sessions:
                        q = self.sessions[SID].session_in

                        if cmd:
                            yield from q.put(cmd)

    @asyncio.coroutine
    def cancel_tasks(self):

        
        while self.isrunning():
            yield from asyncio.sleep(.1)
        
        
        for task in asyncio.Task.all_tasks():
            task.cancel()

        self.server.close()

    def run_module(self):

        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        self.loop = asyncio.get_event_loop()

        self.server = self.loop.run_until_complete(asyncio.start_server(self.client_connected_handler, self.options.IP, self.options.PORT))
        asyncio.async(self.cancel_tasks())    # send server and call stop if not isrunning()

        try:

            self.loop.run_forever()
        
        except KeyboardInterrupt:

            pass

        finally:
            
            self.stopper.set()
            loop.run_until_complete(self.cancel_tasks())
            self.server.close()
            loop.run_until_complete(server.wait_closed())
            loop.stop()
            loop.close()


     
