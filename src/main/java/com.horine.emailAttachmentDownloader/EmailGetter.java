package com.horine.emailAttachmentDownloader;

import com.sun.mail.util.BASE64DecoderStream;
import com.sun.mail.util.MailConnectException;

import javax.mail.*;
import java.io.*;
import java.util.Properties;

public class EmailGetter {

    private String filename;
    private String host;
    private String storeType;
    private String user;
    private String password;
    private Properties properties;

    private static ImageSaver imageSaver;

    /*
    @param host "pop.gmail.com";
    @param mailStoreType = "pop3";
    @param username = "abc@gmail.com";
    @param password = "*****";
     */

    EmailGetter(String filename, String pop3Host, String storeType, String user, String password, ImageSaver imageSaver) {
        this.filename = filename;
        this.host = pop3Host;
        this.storeType = storeType;
        this.user = user;
        this.password = password;

        // create properties field
        properties = new Properties();
        properties.put("mail.store.protocol", "pop3");
        properties.put("mail.pop3.host", host);
        properties.put("mail.pop3.port", "995");
        properties.put("mail.pop3.starttls.enable", "true");

        this.imageSaver = imageSaver;
    }
    DisplayElem fetch(String[] keywords) {
        int numImages = 0;
        int numEmails = 0;
        int presavedImages = 0;
        try {
            Session emailSession = Session.getDefaultInstance(properties);

            // create the POP3 store object and connect with the pop server
            Store store = emailSession.getStore("pop3s");

            store.connect(host, user, password);

            // create the folder object and open it
            Folder emailFolder = store.getFolder("INBOX");
            emailFolder.open(Folder.READ_ONLY);

            BufferedReader reader = new BufferedReader(new InputStreamReader(System.in));

            // retrieve the messages from the folder in an array and print it
            Message[] messages = emailFolder.getMessages();
            System.out.println("messages.length---" + messages.length);
            if (keywords.length > 1) {  //if there are keywords in the array, check all incoming messages against them
                for (int i = 0; i < messages.length; i++) {
                    Message message = messages[i];
                    System.out.println("Message " + i + ": " + messages[i].getSubject());
                    System.out.print("Matches: ");
                    int matches = 0;
                    for (int j = 0; j < keywords.length; j++) { //check the message subject against all of the keywords in the array
                        if (message.getSubject() == null){
                            continue;
                        }
                        else if (message.getSubject().toLowerCase().contains(keywords[j])) {
                            matches++;
                            System.out.print(keywords[j] + " ");
                        }
                    }
                    if (matches >= 1) { //if there is at least one match, dsunset-trail-order-of-the-arrow@googlegroups.comownload the message
                        //System.out.println("---------------------------------");
                        int[] ret = writePart(message);
                        numImages += ret[0];
                        presavedImages += ret[1];
                        numEmails += 1;
                    } else {    //otherwise, move on
                        System.out.println("NO MATCHES");
                    }
                }
            }
            else if (keywords.length <= 1){ //if there are no keywords, accept everything
                System.out.println("NO Keywords!");
                for (int i = 0; i < messages.length; i++) {
                    //System.out.println("---------------------------------");
                    int[] ret = writePart(messages[i]);
                    numImages += ret[0];
                    presavedImages += ret[1];
                }
                numEmails = messages.length;
            }

            // close the store and folder objects
            emailFolder.close(false);
            store.close();

        } catch (NoSuchProviderException e) {
            e.printStackTrace();
        } catch (MailConnectException e){
            System.out.println("Could not connect!");
            e.printStackTrace();
        } catch (MessagingException e) {
            e.printStackTrace();
        } catch (IOException e) {
            e.printStackTrace();
        } catch (Exception e) {
            e.printStackTrace();
        }
        String text = "";
        if (keywords.length <= 1){text =  "Ran " + filename + " and found " + numEmails + " e-mails";}
        else {                    text =  "Ran " + filename + " and found " + numEmails + " matching e-mails";}
        if (numEmails > 0){       text += " with " + numImages + " new images";}
        if (presavedImages > 0)  {text += " and " + presavedImages + " existing images";}
        if (numImages > 0){       text += ". Pictures saved to " + imageSaver.getFolderPath();}
        return new DisplayElem(text);
    }

    /*
     * This method checks for content-type
     * based on which, it processes and
     * fetches the content of the message
     */
    public static int[] writePart(Part p) throws Exception {
        int numImages = 0;
        int presavedImages = 0;
        if (p instanceof Message) {
            //Call method writeEnvelope
            writeEnvelope((Message) p);
        }

        System.out.println("----------------------------");
        System.out.println("CONTENT-TYPE: " + p.getContentType());

        //check if the content is plain text
        if (p.isMimeType("text/plain")) {
            System.out.println("This is plain text");
            System.out.println("---------------------------");
            //System.out.println((String) p.getContent());
        }
        //check if the content has attachment
        else if (p.isMimeType("multipart/*")) {
            System.out.println("This is a Multipart");
            System.out.println("---------------------------");
            Multipart mp = (Multipart) p.getContent();
            int count = mp.getCount();
            for (int i = 0; i < count; i++) {
                int[] ret = writePart(mp.getBodyPart(i));
                numImages += ret[0];
                presavedImages += ret[1];
            }
        }
        //check if the content is a nested message
        else if (p.isMimeType("message/rfc822")) {
            System.out.println("This is a Nested Message");
            System.out.println("---------------------------");
            int[] ret =  writePart((Part) p.getContent());
            numImages += ret[0];
            presavedImages += ret[1];
        }

        //check if the content is an inline image
        else if (p.getContentType().contains("image/")) {
            System.out.println("content type" + p.getContentType());
            // write the image to a file
            String fileType = p.getContentType().split("/")[1].split(";")[0];
            String filename = p.getFileName();
            System.out.println(fileType);
            boolean alreadyExists = imageSaver.saveImage((BASE64DecoderStream) p.getContent(), filename, fileType);
            if (alreadyExists){
                presavedImages += 1;
            }
            else{
                numImages += 1;
            }
        }
        /*
        else {
            Object o = p.getContent();
            if (o instanceof String) {
                System.out.println("This is a string");
                System.out.println("---------------------------");
                //System.out.println((String) o);
            }
            else if (o instanceof InputStream) {
                System.out.println("This is just an input stream");
                System.out.println("---------------------------");
                InputStream is = (InputStream) o;
                is = (InputStream) o;
                int c;
                while ((c = is.read()) != -1)
                    System.out.write(c);
            }
            else {
                System.out.println("This is an unknown type");
                System.out.println("---------------------------");
                System.out.println(o.toString());
            }
        }
        */
        int[] ret = {numImages,presavedImages};
    return ret;
    }
    /*
     * This method would print FROM,TO and SUBJECT of the message
     */
    public static void writeEnvelope(Message m) throws Exception {
        System.out.println("This is the message envelope");
        System.out.println("---------------------------");
        Address[] a;

        // FROM
        if ((a = m.getFrom()) != null) {
            for (int j = 0; j < a.length; j++)
                System.out.println("FROM: " + a[j].toString());
        }

        // TO
        /*
        if ((a = m.getRecipients(Message.RecipientType.TO)) != null) {
            for (int j = 0; j < a.length; j++)
                System.out.println("TO: " + a[j].toString());
        }
        */

        // SUBJECT
        if (m.getSubject() != null)
            System.out.println("SUBJECT: " + m.getSubject());

    }
}
